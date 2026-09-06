#pragma once

#pragma warning(push)
#pragma warning(disable : 4201)
#include <ntddk.h>
#include <kbdmou.h>
#include <ntddkbd.h>
#include <wdf.h>
#pragma warning(pop)

#include "tappy_filter_public.h"

#define TAPPY_FILTER_RING_CAPACITY 1024UL

typedef struct _TAPPY_FILTER_DEVICE_CONTEXT
{
    CONNECT_DATA UpperConnection;
    volatile LONG ConnectionInstalled;
    WDFSPINLOCK StateLock;
    WDFTIMER WatchdogTimer;
    WDFDEVICE ControlDevice;
    BOOLEAN SessionActive;
    BOOLEAN WatchdogArmed;
    UCHAR ReservedBytes[2];
    ULONGLONG TokenLow;
    ULONGLONG TokenHigh;
    ULONGLONG PolicyGeneration;
    ULONGLONG LastHeartbeatInterruptTime;
    ULONGLONG LastSequence;
    ULONGLONG RingOverflowCount;
    ULONG EffectiveMode;
    ULONG Role;
    ULONG WatchdogTimeoutMilliseconds;
    ULONG RingReadIndex;
    ULONG RingCount;
    TAPPY_FILTER_EVENT_V1 Ring[TAPPY_FILTER_RING_CAPACITY];
} TAPPY_FILTER_DEVICE_CONTEXT, *PTAPPY_FILTER_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(TAPPY_FILTER_DEVICE_CONTEXT, TappyGetDeviceContext)

typedef struct _TAPPY_FILTER_CONTROL_CONTEXT
{
    WDFDEVICE ParentDevice;
} TAPPY_FILTER_CONTROL_CONTEXT, *PTAPPY_FILTER_CONTROL_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(TAPPY_FILTER_CONTROL_CONTEXT, TappyGetControlContext)

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD TappyEvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_INTERNAL_DEVICE_CONTROL TappyEvtIoInternalDeviceControl;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL TappyEvtIoDeviceControl;
EVT_WDF_DEVICE_FILE_CREATE TappyEvtFileCreate;
EVT_WDF_FILE_CLOSE TappyEvtFileClose;
EVT_WDF_TIMER TappyEvtWatchdogTimer;

NTSTATUS
TappyCreateControlDevice(
    _In_ WDFDEVICE ParentDevice
    );

VOID
TappyKeyboardServiceCallback(
    _In_ PDEVICE_OBJECT DeviceObject,
    _In_ PKEYBOARD_INPUT_DATA InputDataStart,
    _In_ PKEYBOARD_INPUT_DATA InputDataEnd,
    _Inout_ PULONG InputDataConsumed
    );
