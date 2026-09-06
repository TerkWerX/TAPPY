#include <initguid.h>
#include "driver.h"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT, DriverEntry)
#pragma alloc_text(PAGE, TappyEvtDeviceAdd)
#pragma alloc_text(PAGE, TappyEvtIoInternalDeviceControl)
#pragma alloc_text(PAGE, TappyCreateControlDevice)
#endif

static volatile LONG TappyControlInstanceNumber;

static VOID
TappyResetToPassThroughLocked(
    _Inout_ PTAPPY_FILTER_DEVICE_CONTEXT Context,
    _In_ BOOLEAN ClearSession
    )
{
    Context->EffectiveMode = TappyFilterModePassThrough;
    Context->WatchdogArmed = FALSE;
    Context->WatchdogTimeoutMilliseconds = 0;
    Context->LastHeartbeatInterruptTime = 0;
    Context->RingReadIndex = 0;
    Context->RingCount = 0;

    if (ClearSession)
    {
        Context->SessionActive = FALSE;
        Context->Role = TappyFilterRoleUnassigned;
        Context->TokenLow = 0;
        Context->TokenHigh = 0;
    }
}

static BOOLEAN
TappyTokenMatchesLocked(
    _In_ PTAPPY_FILTER_DEVICE_CONTEXT Context,
    _In_ ULONGLONG TokenLow,
    _In_ ULONGLONG TokenHigh
    )
{
    return Context->SessionActive &&
        Context->TokenLow == TokenLow &&
        Context->TokenHigh == TokenHigh;
}

static BOOLEAN
TappyLeaseIsCurrentLocked(
    _In_ PTAPPY_FILTER_DEVICE_CONTEXT Context,
    _In_ ULONGLONG Now
    )
{
    ULONGLONG timeout100Nanoseconds;

    if (!Context->SessionActive || !Context->WatchdogArmed ||
        Context->EffectiveMode != TappyFilterModeCaptureAndSuppress ||
        Context->Role != TappyFilterRoleConfigurableSecondary)
    {
        return FALSE;
    }

    timeout100Nanoseconds = (ULONGLONG)Context->WatchdogTimeoutMilliseconds * 10000ULL;
    return Now >= Context->LastHeartbeatInterruptTime &&
        Now - Context->LastHeartbeatInterruptTime < timeout100Nanoseconds;
}

static VOID
TappyForwardRequest(
    _In_ WDFDEVICE Device,
    _In_ WDFREQUEST Request
    )
{
    WDF_REQUEST_SEND_OPTIONS options;
    NTSTATUS status;

    WDF_REQUEST_SEND_OPTIONS_INIT(&options, WDF_REQUEST_SEND_OPTION_SEND_AND_FORGET);
    if (!WdfRequestSend(Request, WdfDeviceGetIoTarget(Device), &options))
    {
        status = WdfRequestGetStatus(Request);
        WdfRequestComplete(Request, status);
    }
}

static NTSTATUS
TappyRetrieveVersionedInput(
    _In_ WDFREQUEST Request,
    _In_ size_t RequiredSize,
    _Outptr_ PVOID* Buffer
    )
{
    PULONG fields;
    size_t length;
    NTSTATUS status;

    if (RequiredSize < sizeof(ULONG) * 2)
    {
        return STATUS_INVALID_PARAMETER;
    }

    status = WdfRequestRetrieveInputBuffer(Request, RequiredSize, Buffer, &length);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    fields = (PULONG)*Buffer;
    if (length < sizeof(ULONG) * 2)
    {
        return STATUS_BUFFER_TOO_SMALL;
    }

    if (length < RequiredSize || fields[0] != TAPPY_FILTER_PROTOCOL_VERSION || fields[1] != RequiredSize)
    {
        return STATUS_REVISION_MISMATCH;
    }

    return STATUS_SUCCESS;
}

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
{
    WDF_DRIVER_CONFIG config;

    WDF_DRIVER_CONFIG_INIT(&config, TappyEvtDeviceAdd);
    return WdfDriverCreate(
        DriverObject,
        RegistryPath,
        WDF_NO_OBJECT_ATTRIBUTES,
        &config,
        WDF_NO_HANDLE);
}

NTSTATUS
TappyEvtDeviceAdd(
    _In_ WDFDRIVER Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
    )
{
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_TIMER_CONFIG timerConfig;
    WDFDEVICE device;
    PTAPPY_FILTER_DEVICE_CONTEXT context;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(Driver);
    PAGED_CODE();

    WdfFdoInitSetFilter(DeviceInit);
    WdfDeviceInitSetDeviceType(DeviceInit, FILE_DEVICE_KEYBOARD);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, TAPPY_FILTER_DEVICE_CONTEXT);
    status = WdfDeviceCreate(&DeviceInit, &attributes, &device);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    context = TappyGetDeviceContext(device);
    context->EffectiveMode = TappyFilterModePassThrough;
    context->Role = TappyFilterRoleUnassigned;

    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = device;
    status = WdfSpinLockCreate(&attributes, &context->StateLock);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    WDF_TIMER_CONFIG_INIT(&timerConfig, TappyEvtWatchdogTimer);
    timerConfig.AutomaticSerialization = FALSE;
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = device;
    status = WdfTimerCreate(&timerConfig, &attributes, &context->WatchdogTimer);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchParallel);
    queueConfig.EvtIoInternalDeviceControl = TappyEvtIoInternalDeviceControl;
    status = WdfIoQueueCreate(
        device,
        &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES,
        WDF_NO_HANDLE);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    return TappyCreateControlDevice(device);
}

NTSTATUS
TappyCreateControlDevice(
    _In_ WDFDEVICE ParentDevice
    )
{
    DECLARE_CONST_UNICODE_STRING(deviceId, L"TAPPY\\KeyboardFilterControl");
    DECLARE_CONST_UNICODE_STRING(hardwareId, L"TAPPY\\KeyboardFilterControl");
    DECLARE_CONST_UNICODE_STRING(description, L"Tappy keyboard filter control endpoint");
    DECLARE_CONST_UNICODE_STRING(location, L"Tappy per-device keyboard filter");
    DECLARE_CONST_UNICODE_STRING(systemOnlySddl, L"D:P(A;;GA;;;SY)");
    PWDFDEVICE_INIT childInit;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_FILEOBJECT_CONFIG fileConfig;
    WDFDEVICE childDevice;
    PTAPPY_FILTER_CONTROL_CONTEXT controlContext;
    PTAPPY_FILTER_DEVICE_CONTEXT parentContext;
    WCHAR instanceBuffer[16];
    UNICODE_STRING instanceId;
    ULONG instanceNumber;
    NTSTATUS status;

    PAGED_CODE();

    childInit = WdfPdoInitAllocate(ParentDevice);
    if (childInit == NULL)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    WdfDeviceInitSetDeviceType(childInit, FILE_DEVICE_UNKNOWN);
    WdfDeviceInitSetCharacteristics(
        childInit,
        FILE_AUTOGENERATED_DEVICE_NAME | FILE_DEVICE_SECURE_OPEN,
        FALSE);
    WdfDeviceInitSetExclusive(childInit, TRUE);

    status = WdfDeviceInitAssignSDDLString(childInit, &systemOnlySddl);
    if (!NT_SUCCESS(status))
    {
        goto ExitFailure;
    }

    status = WdfPdoInitAssignRawDevice(childInit, &GUID_DEVCLASS_TAPPY_KEYBOARD_FILTER);
    if (!NT_SUCCESS(status))
    {
        goto ExitFailure;
    }

    status = WdfPdoInitAssignDeviceID(childInit, &deviceId);
    if (!NT_SUCCESS(status))
    {
        goto ExitFailure;
    }

    status = WdfPdoInitAddHardwareID(childInit, &hardwareId);
    if (!NT_SUCCESS(status))
    {
        goto ExitFailure;
    }

    instanceNumber = (ULONG)InterlockedIncrement(&TappyControlInstanceNumber);
    RtlInitEmptyUnicodeString(&instanceId, instanceBuffer, sizeof(instanceBuffer));
    status = RtlIntegerToUnicodeString(instanceNumber, 10, &instanceId);
    if (!NT_SUCCESS(status))
    {
        goto ExitFailure;
    }

    status = WdfPdoInitAssignInstanceID(childInit, &instanceId);
    if (!NT_SUCCESS(status))
    {
        goto ExitFailure;
    }

    status = WdfPdoInitAddDeviceText(childInit, &description, &location, 0x409);
    if (!NT_SUCCESS(status))
    {
        goto ExitFailure;
    }
    WdfPdoInitSetDefaultLocale(childInit, 0x409);

    WDF_FILEOBJECT_CONFIG_INIT(
        &fileConfig,
        TappyEvtFileCreate,
        TappyEvtFileClose,
        WDF_NO_EVENT_CALLBACK);
    WdfDeviceInitSetFileObjectConfig(childInit, &fileConfig, WDF_NO_OBJECT_ATTRIBUTES);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, TAPPY_FILTER_CONTROL_CONTEXT);
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfDeviceCreate(&childInit, &attributes, &childDevice);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    controlContext = TappyGetControlContext(childDevice);
    controlContext->ParentDevice = ParentDevice;

    status = WdfDeviceCreateDeviceInterface(
        childDevice,
        &GUID_DEVINTERFACE_TAPPY_KEYBOARD_FILTER,
        NULL);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchParallel);
    queueConfig.EvtIoDeviceControl = TappyEvtIoDeviceControl;
    status = WdfIoQueueCreate(
        childDevice,
        &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES,
        WDF_NO_HANDLE);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    status = WdfFdoAddStaticChild(ParentDevice, childDevice);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    parentContext = TappyGetDeviceContext(ParentDevice);
    parentContext->ControlDevice = childDevice;
    return STATUS_SUCCESS;

ExitFailure:
    WdfDeviceInitFree(childInit);
    return status;
}

VOID
TappyEvtIoInternalDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
    )
{
    WDFDEVICE device;
    PTAPPY_FILTER_DEVICE_CONTEXT context;
    PCONNECT_DATA connection;
    size_t connectionLength;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);
    PAGED_CODE();

    device = WdfIoQueueGetDevice(Queue);
    context = TappyGetDeviceContext(device);

    if (IoControlCode == IOCTL_INTERNAL_KEYBOARD_CONNECT)
    {
        if (InterlockedCompareExchange(&context->ConnectionInstalled, 1, 0) != 0)
        {
            WdfRequestComplete(Request, STATUS_SHARING_VIOLATION);
            return;
        }

        status = WdfRequestRetrieveInputBuffer(
            Request,
            sizeof(CONNECT_DATA),
            (PVOID*)&connection,
            &connectionLength);
        if (!NT_SUCCESS(status))
        {
            InterlockedExchange(&context->ConnectionInstalled, 0);
            WdfRequestComplete(Request, status);
            return;
        }

        if (connectionLength < sizeof(CONNECT_DATA) || connection->ClassService == NULL)
        {
            InterlockedExchange(&context->ConnectionInstalled, 0);
            WdfRequestComplete(Request, STATUS_INVALID_PARAMETER);
            return;
        }

        context->UpperConnection = *connection;
        connection->ClassDeviceObject = WdfDeviceWdmGetDeviceObject(device);
        connection->ClassService = (PVOID)(ULONG_PTR)TappyKeyboardServiceCallback;
    }
    else if (IoControlCode == IOCTL_INTERNAL_KEYBOARD_DISCONNECT)
    {
        WdfRequestComplete(Request, STATUS_NOT_SUPPORTED);
        return;
    }

    TappyForwardRequest(device, Request);
}

VOID
TappyEvtFileCreate(
    _In_ WDFDEVICE Device,
    _In_ WDFREQUEST Request,
    _In_ WDFFILEOBJECT FileObject
    )
{
    PTAPPY_FILTER_CONTROL_CONTEXT controlContext;
    PTAPPY_FILTER_DEVICE_CONTEXT context;

    UNREFERENCED_PARAMETER(FileObject);
    controlContext = TappyGetControlContext(Device);
    context = TappyGetDeviceContext(controlContext->ParentDevice);
    WdfSpinLockAcquire(context->StateLock);
    TappyResetToPassThroughLocked(context, TRUE);
    WdfSpinLockRelease(context->StateLock);
    WdfRequestComplete(Request, STATUS_SUCCESS);
}

VOID
TappyEvtFileClose(
    _In_ WDFFILEOBJECT FileObject
    )
{
    WDFDEVICE device;
    PTAPPY_FILTER_CONTROL_CONTEXT controlContext;
    PTAPPY_FILTER_DEVICE_CONTEXT context;

    device = WdfFileObjectGetDevice(FileObject);
    controlContext = TappyGetControlContext(device);
    context = TappyGetDeviceContext(controlContext->ParentDevice);
    WdfSpinLockAcquire(context->StateLock);
    TappyResetToPassThroughLocked(context, TRUE);
    WdfSpinLockRelease(context->StateLock);
}

VOID
TappyEvtWatchdogTimer(
    _In_ WDFTIMER Timer
    )
{
    WDFDEVICE device;
    PTAPPY_FILTER_DEVICE_CONTEXT context;
    ULONGLONG now;

    device = (WDFDEVICE)WdfTimerGetParentObject(Timer);
    context = TappyGetDeviceContext(device);
    now = KeQueryInterruptTime();

    WdfSpinLockAcquire(context->StateLock);
    if (context->WatchdogArmed && !TappyLeaseIsCurrentLocked(context, now))
    {
        TappyResetToPassThroughLocked(context, TRUE);
    }
    WdfSpinLockRelease(context->StateLock);
}

VOID
TappyEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
    )
{
    WDFDEVICE controlDevice;
    PTAPPY_FILTER_CONTROL_CONTEXT controlContext;
    PTAPPY_FILTER_DEVICE_CONTEXT context;
    PTAPPY_FILTER_SESSION_REQUEST_V1 sessionRequest;
    PTAPPY_FILTER_POLICY_REQUEST_V1 policyRequest;
    PTAPPY_FILTER_HEARTBEAT_REQUEST_V1 heartbeatRequest;
    PTAPPY_FILTER_READ_REQUEST_V1 readRequest;
    PTAPPY_FILTER_STATUS_V1 statusOutput;
    PTAPPY_FILTER_EVENT_V1 eventOutput;
    ULONG outputCapacity;
    ULONG eventCount;
    ULONG index;
    BOOLEAN startWatchdog;
    ULONG watchdogTimeout;
    NTSTATUS status;
    size_t information;

    UNREFERENCED_PARAMETER(InputBufferLength);
    controlDevice = WdfIoQueueGetDevice(Queue);
    controlContext = TappyGetControlContext(controlDevice);
    context = TappyGetDeviceContext(controlContext->ParentDevice);
    status = STATUS_INVALID_DEVICE_REQUEST;
    information = 0;
    startWatchdog = FALSE;
    watchdogTimeout = 0;

    switch (IoControlCode)
    {
    case IOCTL_TAPPY_FILTER_QUERY_STATUS:
        status = WdfRequestRetrieveOutputBuffer(
            Request,
            sizeof(TAPPY_FILTER_STATUS_V1),
            (PVOID*)&statusOutput,
            NULL);
        if (!NT_SUCCESS(status))
        {
            break;
        }

        WdfSpinLockAcquire(context->StateLock);
        statusOutput->ProtocolVersion = TAPPY_FILTER_PROTOCOL_VERSION;
        statusOutput->EffectiveMode = context->EffectiveMode;
        statusOutput->Role = context->Role;
        statusOutput->WatchdogArmed = context->WatchdogArmed;
        statusOutput->WatchdogTimeoutMilliseconds = context->WatchdogTimeoutMilliseconds;
        statusOutput->Reserved = 0;
        statusOutput->PolicyGeneration = context->PolicyGeneration;
        statusOutput->LastSequence = context->LastSequence;
        statusOutput->RingOverflowCount = context->RingOverflowCount;
        WdfSpinLockRelease(context->StateLock);
        information = sizeof(TAPPY_FILTER_STATUS_V1);
        status = STATUS_SUCCESS;
        break;

    case IOCTL_TAPPY_FILTER_BEGIN_AUTHENTICATED_SESSION:
        status = TappyRetrieveVersionedInput(
            Request,
            sizeof(TAPPY_FILTER_SESSION_REQUEST_V1),
            (PVOID*)&sessionRequest);
        if (!NT_SUCCESS(status))
        {
            break;
        }
        if (sessionRequest->TokenLow == 0 && sessionRequest->TokenHigh == 0)
        {
            status = STATUS_INVALID_PARAMETER;
            break;
        }

        WdfSpinLockAcquire(context->StateLock);
        TappyResetToPassThroughLocked(context, TRUE);
        context->TokenLow = sessionRequest->TokenLow;
        context->TokenHigh = sessionRequest->TokenHigh;
        context->SessionActive = TRUE;
        WdfSpinLockRelease(context->StateLock);
        status = STATUS_SUCCESS;
        break;

    case IOCTL_TAPPY_FILTER_SET_INSTANCE_POLICY:
        status = TappyRetrieveVersionedInput(
            Request,
            sizeof(TAPPY_FILTER_POLICY_REQUEST_V1),
            (PVOID*)&policyRequest);
        if (!NT_SUCCESS(status))
        {
            break;
        }
        if (policyRequest->Reserved != 0 ||
            policyRequest->Role > TappyFilterRoleConfigurableSecondary ||
            policyRequest->RequestedMode > TappyFilterModeCaptureAndSuppress ||
            policyRequest->PolicyGeneration == 0 ||
            (policyRequest->Role != TappyFilterRoleConfigurableSecondary &&
                policyRequest->RequestedMode != TappyFilterModePassThrough) ||
            (policyRequest->RequestedMode == TappyFilterModeCaptureAndSuppress &&
                (policyRequest->WatchdogTimeoutMilliseconds < TAPPY_FILTER_MINIMUM_WATCHDOG_MS ||
                 policyRequest->WatchdogTimeoutMilliseconds > TAPPY_FILTER_MAXIMUM_WATCHDOG_MS)))
        {
            status = STATUS_INVALID_PARAMETER;
            break;
        }

        WdfSpinLockAcquire(context->StateLock);
        if (!TappyTokenMatchesLocked(context, policyRequest->TokenLow, policyRequest->TokenHigh))
        {
            status = STATUS_ACCESS_DENIED;
        }
        else if (policyRequest->PolicyGeneration <= context->PolicyGeneration)
        {
            status = STATUS_REVISION_MISMATCH;
        }
        else
        {
            context->PolicyGeneration = policyRequest->PolicyGeneration;
            context->Role = policyRequest->Role;
            context->EffectiveMode = policyRequest->RequestedMode;
            context->RingReadIndex = 0;
            context->RingCount = 0;
            if (policyRequest->RequestedMode == TappyFilterModeCaptureAndSuppress)
            {
                context->WatchdogTimeoutMilliseconds = policyRequest->WatchdogTimeoutMilliseconds;
                context->LastHeartbeatInterruptTime = KeQueryInterruptTime();
                context->WatchdogArmed = TRUE;
                startWatchdog = TRUE;
                watchdogTimeout = policyRequest->WatchdogTimeoutMilliseconds;
            }
            else
            {
                context->WatchdogArmed = FALSE;
                context->WatchdogTimeoutMilliseconds = 0;
                context->LastHeartbeatInterruptTime = 0;
            }
            status = STATUS_SUCCESS;
        }
        WdfSpinLockRelease(context->StateLock);
        break;

    case IOCTL_TAPPY_FILTER_HEARTBEAT:
        status = TappyRetrieveVersionedInput(
            Request,
            sizeof(TAPPY_FILTER_HEARTBEAT_REQUEST_V1),
            (PVOID*)&heartbeatRequest);
        if (!NT_SUCCESS(status))
        {
            break;
        }

        WdfSpinLockAcquire(context->StateLock);
        if (!TappyTokenMatchesLocked(context, heartbeatRequest->TokenLow, heartbeatRequest->TokenHigh))
        {
            status = STATUS_ACCESS_DENIED;
        }
        else if (!context->WatchdogArmed ||
            heartbeatRequest->PolicyGeneration != context->PolicyGeneration)
        {
            status = STATUS_INVALID_DEVICE_STATE;
        }
        else
        {
            context->LastHeartbeatInterruptTime = KeQueryInterruptTime();
            startWatchdog = TRUE;
            watchdogTimeout = context->WatchdogTimeoutMilliseconds;
            status = STATUS_SUCCESS;
        }
        WdfSpinLockRelease(context->StateLock);
        break;

    case IOCTL_TAPPY_FILTER_READ_EVENTS:
        status = TappyRetrieveVersionedInput(
            Request,
            sizeof(TAPPY_FILTER_READ_REQUEST_V1),
            (PVOID*)&readRequest);
        if (!NT_SUCCESS(status))
        {
            break;
        }
        if (readRequest->Reserved != 0 || readRequest->MaximumEvents == 0 ||
            readRequest->MaximumEvents > TAPPY_FILTER_MAXIMUM_EVENTS_PER_READ)
        {
            status = STATUS_INVALID_PARAMETER;
            break;
        }

        outputCapacity = (ULONG)(OutputBufferLength / sizeof(TAPPY_FILTER_EVENT_V1));
        if (outputCapacity == 0)
        {
            status = STATUS_BUFFER_TOO_SMALL;
            break;
        }
        if (outputCapacity > readRequest->MaximumEvents)
        {
            outputCapacity = readRequest->MaximumEvents;
        }

        status = WdfRequestRetrieveOutputBuffer(
            Request,
            sizeof(TAPPY_FILTER_EVENT_V1),
            (PVOID*)&eventOutput,
            NULL);
        if (!NT_SUCCESS(status))
        {
            break;
        }

        WdfSpinLockAcquire(context->StateLock);
        if (!TappyTokenMatchesLocked(context, readRequest->TokenLow, readRequest->TokenHigh))
        {
            status = STATUS_ACCESS_DENIED;
            eventCount = 0;
        }
        else
        {
            eventCount = context->RingCount < outputCapacity ? context->RingCount : outputCapacity;
            for (index = 0; index < eventCount; ++index)
            {
                eventOutput[index] = context->Ring[context->RingReadIndex];
                context->RingReadIndex = (context->RingReadIndex + 1) % TAPPY_FILTER_RING_CAPACITY;
            }
            context->RingCount -= eventCount;
            status = STATUS_SUCCESS;
        }
        WdfSpinLockRelease(context->StateLock);
        information = (size_t)eventCount * sizeof(TAPPY_FILTER_EVENT_V1);
        break;

    case IOCTL_TAPPY_FILTER_FORCE_FAIL_OPEN:
        status = TappyRetrieveVersionedInput(
            Request,
            sizeof(TAPPY_FILTER_SESSION_REQUEST_V1),
            (PVOID*)&sessionRequest);
        if (!NT_SUCCESS(status))
        {
            break;
        }

        WdfSpinLockAcquire(context->StateLock);
        if (!TappyTokenMatchesLocked(context, sessionRequest->TokenLow, sessionRequest->TokenHigh))
        {
            status = STATUS_ACCESS_DENIED;
        }
        else
        {
            TappyResetToPassThroughLocked(context, TRUE);
            status = STATUS_SUCCESS;
        }
        WdfSpinLockRelease(context->StateLock);
        break;
    }

    if (!NT_SUCCESS(status) && IoControlCode != IOCTL_TAPPY_FILTER_QUERY_STATUS)
    {
        WdfSpinLockAcquire(context->StateLock);
        TappyResetToPassThroughLocked(context, TRUE);
        WdfSpinLockRelease(context->StateLock);
        startWatchdog = FALSE;
    }

    if (startWatchdog)
    {
        WdfTimerStart(context->WatchdogTimer, WDF_REL_TIMEOUT_IN_MS(watchdogTimeout));
    }

    WdfRequestCompleteWithInformation(Request, status, information);
}

VOID
TappyKeyboardServiceCallback(
    _In_ PDEVICE_OBJECT DeviceObject,
    _In_ PKEYBOARD_INPUT_DATA InputDataStart,
    _In_ PKEYBOARD_INPUT_DATA InputDataEnd,
    _Inout_ PULONG InputDataConsumed
    )
{
    WDFDEVICE device;
    PTAPPY_FILTER_DEVICE_CONTEXT context;
    PSERVICE_CALLBACK_ROUTINE upperService;
    PKEYBOARD_INPUT_DATA packet;
    ULONG packetCount;
    ULONG writeIndex;
    BOOLEAN suppress;
    ULONGLONG now;

    device = WdfWdmDeviceGetWdfDeviceHandle(DeviceObject);
    context = TappyGetDeviceContext(device);
    upperService = (PSERVICE_CALLBACK_ROUTINE)(ULONG_PTR)context->UpperConnection.ClassService;
    suppress = FALSE;

    if (InputDataEnd > InputDataStart)
    {
        packetCount = (ULONG)(InputDataEnd - InputDataStart);
        now = KeQueryInterruptTime();

        WdfSpinLockAcquire(context->StateLock);
        if (TappyLeaseIsCurrentLocked(context, now))
        {
            if (packetCount > TAPPY_FILTER_RING_CAPACITY - context->RingCount)
            {
                ++context->RingOverflowCount;
                TappyResetToPassThroughLocked(context, TRUE);
            }
            else
            {
                for (packet = InputDataStart; packet < InputDataEnd; ++packet)
                {
                    writeIndex = (context->RingReadIndex + context->RingCount) % TAPPY_FILTER_RING_CAPACITY;
                    context->Ring[writeIndex].Sequence = ++context->LastSequence;
                    context->Ring[writeIndex].PolicyGeneration = context->PolicyGeneration;
                    context->Ring[writeIndex].MakeCode = packet->MakeCode;
                    context->Ring[writeIndex].Flags = packet->Flags &
                        (TAPPY_FILTER_EVENT_BREAK | TAPPY_FILTER_EVENT_E0 | TAPPY_FILTER_EVENT_E1);
                    context->Ring[writeIndex].UnitId = packet->UnitId;
                    context->Ring[writeIndex].Reserved = 0;
                    ++context->RingCount;
                }
                suppress = TRUE;
            }
        }
        else if (context->EffectiveMode == TappyFilterModeCaptureAndSuppress)
        {
            TappyResetToPassThroughLocked(context, TRUE);
        }
        WdfSpinLockRelease(context->StateLock);

        if (suppress)
        {
            *InputDataConsumed = packetCount;
            return;
        }
    }

    if (upperService != NULL)
    {
        upperService(
            context->UpperConnection.ClassDeviceObject,
            InputDataStart,
            InputDataEnd,
            InputDataConsumed);
    }
}
