function refreshDevices() {
    var payload = {};
    payload.property_inspector = 'refreshDevices';
    sendPayloadToPlugin(payload);
}