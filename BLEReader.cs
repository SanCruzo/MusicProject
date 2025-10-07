using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEngine;
using static BleApi;

public class BLEReader : MonoBehaviour
{
    public string targetDeviceName = "ESP32_BLE_IMU";
    public string targetServiceUUID = "12345678-1234-1234-1234-1234567890ab";
    public string targetCharacteristicUUID = "abcdefab-1234-1234-1234-abcdefabcdef";

    private string connectedDeviceId = "";
    public GameObject cube;
	private Vector3 gyroData = Vector3.zero;
	[SerializeField] private ShakerSynth shaker;

    private Thread bleThread;
    private bool running = true;

    void Start()
    {
		if (shaker == null)
			shaker = GetComponent<ShakerSynth>();

        bleThread = new Thread(BleLoop);
        bleThread.Start();
    }

    void Update()
    {
		Vector3 deltaRotation = gyroData * Time.deltaTime * 0.001f;
        cube.transform.Rotate(deltaRotation, Space.Self);
		if (shaker != null)
			shaker.Drive(gyroData);
    }

    void OnApplicationQuit()
    {
        running = false;
        BleApi.Quit();
        if (bleThread != null && bleThread.IsAlive)
            bleThread.Join();
    }

    private string NormalizeUUID(string uuid)
    {
        return uuid.Trim().ToLower().Replace("{", "").Replace("}", "");
    }

    void BleLoop()
    {
        // This is the main reconnection loop. It runs as long as the app is running.
        while (running)
        {
            // If we are not connected, start scanning
            if (connectedDeviceId == "")
            {
                BleApi.StartDeviceScan();
                UnityEngine.Debug.Log("[BLE] Scanning started.");

                // This inner loop handles the scanning and connection attempt
                while (running && connectedDeviceId == "")
                {
                    BleApi.DeviceUpdate device = new BleApi.DeviceUpdate();
                    if (BleApi.PollDevice(ref device, false) == BleApi.ScanStatus.AVAILABLE)
                    {
                        UnityEngine.Debug.Log("[BLE] Found device: " + device.name);
                        if (device.name == targetDeviceName)
                        {
                            connectedDeviceId = device.id;
                            UnityEngine.Debug.Log("[BLE] Target device found: " + connectedDeviceId);
                            BleApi.StopDeviceScan();

                            // Once connected, we need to find the correct service and characteristic
                            BleApi.ScanServices(connectedDeviceId);
                            BleApi.Service service;
                            while (BleApi.PollService(out service, true) == BleApi.ScanStatus.AVAILABLE)
                            {
                                UnityEngine.Debug.Log("[BLE] Found service: {" + service.uuid + "}");
                                if (NormalizeUUID(service.uuid) == NormalizeUUID(targetServiceUUID))
                                {
                                    BleApi.ScanCharacteristics(connectedDeviceId, service.uuid);
                                    BleApi.Characteristic characteristic;
                                    while (BleApi.PollCharacteristic(out characteristic, true) == BleApi.ScanStatus.AVAILABLE)
                                    {
                                        UnityEngine.Debug.Log("[BLE] Found characteristic: {" + characteristic.uuid + "}, Description: " + characteristic.userDescription);
                                        if (NormalizeUUID(characteristic.uuid) == NormalizeUUID(targetCharacteristicUUID))
                                        {
                                            bool subscribed = BleApi.SubscribeCharacteristic(connectedDeviceId, service.uuid, characteristic.uuid, true);
                                            UnityEngine.Debug.Log("[BLE] Successfully subscribed to target characteristic? " + subscribed);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    Thread.Sleep(100); // Wait a moment before checking for devices again
                }
            }

            // If we are connected, start polling for data and check for timeouts
            if (connectedDeviceId != "")
            {
                var timeoutStopwatch = new Stopwatch();
                timeoutStopwatch.Start();

                // This inner loop handles data polling
                while (running && connectedDeviceId != "")
                {
                    BleApi.BLEData data;
                    if (BleApi.PollData(out data, false))
                    {
                        // If we get data, restart the timeout timer
                        timeoutStopwatch.Restart();
                        var value = Encoding.UTF8.GetString(data.buf, 0, data.size);
                        UnityEngine.Debug.Log("[BLE] Received data: " + value);

                        string[] parts = value.Split(',');
                        if (parts.Length == 3 &&
                            float.TryParse(parts[0], out float gx) &&
                            float.TryParse(parts[1], out float gy) &&
                            float.TryParse(parts[2], out float gz))
                        {
                            gyroData = new Vector3(-gx, -gy, -gz);
                        }
                    }

                    // If we haven't received data for 2 seconds, assume we are disconnected
                    if (timeoutStopwatch.ElapsedMilliseconds > 2000)
                    {
                        UnityEngine.Debug.Log("[BLE] Connection timed out. Disconnecting to retry.");
                        connectedDeviceId = "";
                        gyroData = Vector3.zero; // Reset gyro data to stop rotation
                    }

                    Thread.Sleep(20); // Wait a moment before polling for data again
                }
            }
        }
    }
}