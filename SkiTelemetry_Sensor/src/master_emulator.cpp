#include <Arduino.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>

#define SERVICE_UUID        "4fafc201-1fb5-459e-8fcc-c5c9c331914b"
#define CHARACTERISTIC_UUID "beb5483e-36e1-4688-b7f5-ea07361b26a8"

BLECharacteristic *pCharacteristic;
bool deviceConnected = false;

// Struttura Combo: 52 byte totali
struct __attribute__((packed)) SkiDataCombo {
  uint32_t timestamp; 
  float accXL, accYL, accZL;
  float gyroXL, gyroYL, gyroZL;
  float accXR, accYR, accZR;
  float gyroXR, gyroYR, gyroZR;
} dataCombo;

// Callback per monitorare la connessione
class MyServerCallbacks: public BLEServerCallbacks {
    void onConnect(BLEServer* pServer) {
      deviceConnected = true;
      Serial.println(">>> EVENTO: Smartphone connesso, inizio invio dati...");
    };

    void onDisconnect(BLEServer* pServer) {
      deviceConnected = false;
      Serial.println(">>> EVENTO: Smartphone disconnesso!");
      // Riavvia l'advertising per permettere la riconnessione immediata
      pServer->getAdvertising()->start();
      Serial.println(">>> INFO: Advertising riavviato. Pronto per nuova connessione...");
    }
};

void setup() {
  Serial.begin(115200);
  Serial.println("--- AVVIO SISTEMA TELEMETRIA ---");

  BLEDevice::init("MASTER"); 
  BLEServer *pServer = BLEDevice::createServer();
  pServer->setCallbacks(new MyServerCallbacks());

  BLEService *pService = pServer->createService(SERVICE_UUID);
  
  pCharacteristic = pService->createCharacteristic(
                      CHARACTERISTIC_UUID, 
                      BLECharacteristic::PROPERTY_NOTIFY
                    );
  
  pCharacteristic->addDescriptor(new BLE2902());
  pService->start();

  BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
  pAdvertising->addServiceUUID(SERVICE_UUID);
  pAdvertising->setScanResponse(true);
  
  BLEDevice::startAdvertising();
  Serial.println("--- SETUP COMPLETATO: In attesa di connessione... ---");
}

void loop() {
  if (deviceConnected) {
    unsigned long currentMillis = millis();
    float time = currentMillis / 1000.0f;
    float amp = 0.6f; 

    dataCombo.timestamp = currentMillis;

   // -------- PARAMETRI --------
float maxAngle = 45.0f * M_PI / 180.0f;
float freq = 1.0f / 3.0f; // il secondo valore sono i secondi per un ciclo completo (3s -> 0.33Hz)

// -------- CINEMATICA --------
float angle = maxAngle * sin(2 * M_PI * freq * time);
float angularVel = maxAngle * 2 * M_PI * freq * cos(2 * M_PI * freq * time);

// 👉 conversione a gradi/s
float angularVelDeg = angularVel * 180.0f / M_PI;

// -------- SINISTRO --------
dataCombo.gyroXL = 0.0f;
dataCombo.gyroYL = 0.0f;
dataCombo.gyroZL = angularVelDeg;

// Gravità (sempre in G, NON toccare)
dataCombo.accXL = sin(angle);
dataCombo.accYL = cos(angle);
dataCombo.accZL = 0.0f;

// -------- DESTRO --------
dataCombo.gyroXR = 0.0f;
dataCombo.gyroYR = 0.0f;
dataCombo.gyroZR = angularVelDeg;

dataCombo.accXR = sin(angle);
dataCombo.accYR = cos(angle);
dataCombo.accZR = 0.0f;

    // Invio pacchetto Combo (52 byte)
    pCharacteristic->setValue((uint8_t*)&dataCombo, sizeof(SkiDataCombo));
    pCharacteristic->notify();

    /*Serial.print("Dati inviati - TS: ");
    Serial.println(currentMillis);*/
    
    delay(20); // 10Hz
  }
  else {
    // Se non connesso, possiamo rallentare il loop
    delay(1000);
  }
}