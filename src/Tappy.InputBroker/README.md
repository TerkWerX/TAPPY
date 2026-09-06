# Tappy Input Broker

This project is the future LocalSystem boundary for optional exclusive keyboard
input. It currently builds as a Windows-service-capable executable but is
deliberately **status-only**:

- no message opens the keyboard filter;
- no message assigns primary or secondary keyboard roles;
- no message starts or heartbeats suppression;
- no captured input is forwarded;
- the service is not part of the Tappy installer.

The local pipe accepts one client at a time. Its DACL contains LocalSystem and one
installer-selected user SID, it rejects remote-computer clients, and every bounded
frame uses a 256-bit installer-provisioned HMAC key plus a strict sequence number.
The service refuses to start without an explicit bootstrap file and rejects broad,
group, service, and SYSTEM identities.

Run the non-service cryptographic self-test:

```powershell
dotnet run --project src/Tappy.InputBroker/Tappy.InputBroker.csproj `
  -c Release -- --self-test
```

Do not manually install this scaffold as a service. The installer must eventually
provision the bootstrap file with an ACL limited to LocalSystem and the selected
user, and production control commands remain gated on stable per-device association,
signed-client verification, dedicated driver-lab recovery evidence, and Microsoft
driver signing.
