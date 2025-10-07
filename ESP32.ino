#include <BLEDevice.h>
#include <BLEUtils.h>
#include <BLEServer.h>
#include <BLE2902.h> 
#include <Wire.h>
#include <MPU9250_asukiaaa.h>


#define SERVICE_UUID        "12345678-1234-1234-1234-1234567890ab"
#define CHARACTERISTIC_UUID "abcdefab-1234-1234-1234-abcdefabcdef"


MPU9250_asukiaaa mySensor;

float gx_offset = -2.258;
float gy_offset = -1.607;
float gz_offset = -1.912;

BLECharacteristic *pCharacteristic;
bool deviceConnected = false;

// Callback
class MyServerCallbacks: public BLEServerCallbacks {
  void onConnect(BLEServer* pServer) {
    deviceConnected = true;
    Serial.println("BLE connected!");
  }

  void onDisconnect(BLEServer* pServer) {
    deviceConnected = false;
    Serial.println("BLE disconnected.");
  }
};

void setup() {
  Serial.begin(115200);
  Wire.begin();
  mySensor.setWire(&Wire);
  mySensor.beginGyro();

  // BLE Start
  BLEDevice::init("ESP32_BLE_IMU");
  BLEServer *pServer = BLEDevice::createServer();
  pServer->setCallbacks(new MyServerCallbacks());

  BLEService *pService = pServer->createService(SERVICE_UUID);

  pCharacteristic = pService->createCharacteristic(
                      CHARACTERISTIC_UUID,
                      BLECharacteristic::PROPERTY_READ |
                      BLECharacteristic::PROPERTY_NOTIFY
                    );

  // Notify descriptor
  pCharacteristic->addDescriptor(new BLE2902());

  pCharacteristic->setValue("Connection ready.");
  pService->start();
  pServer->getAdvertising()->start();
  Serial.println("BLE IMU service has started.");
}

void loop() {

  mySensor.gyroUpdate();


  
  float gx = mySensor.gyroX() - gx_offset;
  float gy = mySensor.gyroY() - gy_offset;
  float gz = mySensor.gyroZ() - gz_offset;
  if(gx>= -20 && gx <= 20){
    gx=0;
  }
   if(gy>= -20 && gy <= 20){
    gy=0;
  }
   if(gz>= -20 && gz <= 20){
    gz=0;
  }

  String data =  String(gx, 3) + "," + String(gy, 3) + "," + String(gz, 3);


  if (deviceConnected) {
    pCharacteristic->setValue(data.c_str());
    pCharacteristic->notify();
    Serial.println("Data sent: " + data);
  }

  delay(50); // 20Hz
}
