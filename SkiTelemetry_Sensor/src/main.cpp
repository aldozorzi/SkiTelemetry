#include <Arduino.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>

#define SERVICE_UUID        "4fafc201-1fb5-459e-8fcc-c5c9c331914b"
#define CHARACTERISTIC_UUID "beb5483e-36e1-4688-b7f5-ea07361b26a8"

BLECharacteristic *pCharacteristic;

// Struttura DEFINITIVA da 28 byte
struct __attribute__((packed)) SkiData {
  float accX;
  float accY;
  float accZ;
  float gyroX;
  float gyroY;
  float gyroZ;
  uint32_t timestamp;
} data;

class MyServerCallbacks: public BLEServerCallbacks {
    void onConnect(BLEServer* pServer) {
      Serial.println("Dispositivo Connesso");
    };

    void onDisconnect(BLEServer* pServer) {
      Serial.println("Dispositivo Scollegato - Riavvio Advertising...");
      pServer->getAdvertising()->start(); // <--- FONDAMENTALE
    }
};

void setup() {
  Serial.begin(115200);
  
  // Inizializzazione BLE
  BLEDevice::init("SKI_L"); 
  BLEServer *pServer = BLEDevice::createServer();
  
  // Creazione Servizio e Caratteristica
  pServer->setCallbacks(new MyServerCallbacks());
  BLEService *pService = pServer->createService(SERVICE_UUID);
  pCharacteristic = pService->createCharacteristic(
                      CHARACTERISTIC_UUID, 
                      BLECharacteristic::PROPERTY_NOTIFY
                    );
  
  // Aggiunta descrittore per abilitare le notifiche
  pCharacteristic->addDescriptor(new BLE2902());
  
  // Avvio del servizio
  pService->start();

  // CONFIGURAZIONE ADVERTISING (Normalizzata)
  BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
  pAdvertising->addServiceUUID(SERVICE_UUID); // Importante per la visibilità del servizio
  pAdvertising->setScanResponse(true);
  pAdvertising->setMinPreferred(0x06);  
  pAdvertising->setMinPreferred(0x12);
  
  // Avvio definitivo
  BLEDevice::startAdvertising();
  
  Serial.println("Sensore SKI_L pronto e in advertising");
}

void loop() {
  unsigned long currentMillis = millis();
  float time = currentMillis / 1000.0f;

  // Ampiezza costante (es. 0.6f) per avere picchi sempre uguali
  float amplitude = 0.6f; 
  
  // Accelerazione (Sinusoide pura e costante)
  data.accX = 0.0f;
  data.accY = (sin(time) * amplitude); 
  data.accZ = 1.0f; // Manteniamo Z stabile

  // Giroscopio (Derivata costante)
  data.gyroX = (cos(time) * 1.5f);
  data.gyroY = 0.2f;
  data.gyroZ = (sin(time) * 0.8f);

  data.timestamp = currentMillis;

  pCharacteristic->setValue((uint8_t*)&data, sizeof(SkiData));
  pCharacteristic->notify();

  delay(100); 
}