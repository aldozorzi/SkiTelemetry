#include <M5Unified.h>
#include <esp_now.h>
#include <WiFi.h>
#include <driver/rtc_io.h>

// Dizionario dei comandi per la comunicazione Master-Slave
enum Command {
    CMD_STOP  = 0,  // Pausa invio dati
    CMD_START = 1,  // Avvio invio dati
    CMD_SLEEP = 2   // Vai in Light Sleep
};

// MAC ADDRESS del Master che abbiamo appena trovato
uint8_t masterAddress[] = {0x00, 0x4B, 0x12, 0xC0, 0xC7, 0x90};

bool masterWantsData = false;
bool displayOn = false; // Stato iniziale
static unsigned long lastBatUpdate = 0;

struct __attribute__((packed)) SlaveData {
    float accX, accY, accZ;
    float gyroX, gyroY, gyroZ;
} myData;

static float lastCheckX = 0;
static int stallCount = 0;

void goToLightSleep() {
    // 1. Feedback sonoro di spegnimento
    M5.Speaker.tone(1500, 100);
    delay(150);
    M5.Speaker.tone(1000, 100);

    // 2. Spegnimento visivo
    M5.Display.setBrightness(0);
    M5.Display.sleep();
    displayOn = false;

    // 3. Attesa rilascio tasto (se chiamato da pressione locale)
    while (M5.BtnA.isPressed()) {
        M5.update();
        delay(10);
    }
    delay(200); // Debounce finale

    // 4. Configura risveglio e dormi
    esp_sleep_enable_ext0_wakeup(GPIO_NUM_37, 0);
    esp_light_sleep_start();

    // --- IL CHIP SI FERMA QUI ---

    // 5. Logica di RISVEGLIO
    delay(100); // Pausa per stabilizzare l'hardware
    
    M5.Display.setBrightness(0);
    M5.Display.sleep();
    displayOn = true;

    // Feedback sonoro di risveglio
    M5.Speaker.tone(1000, 100);
    delay(150);
    M5.Speaker.tone(1500, 100);

    M5.Imu.begin(); // Forza il re-init del sensore hardware
    M5.Imu.setCalibration(0,0,0);
}

void OnDataRecv(const uint8_t * mac, const uint8_t *incomingData, int len) {
    Serial.println("OnDataRecv");
    if (len == 1) {
        Command cmd = (Command)incomingData[0];

        switch (cmd) {
            case CMD_START:
                Serial.println("CMD_START");
                masterWantsData = true;
                M5.Display.fillCircle(5, 5, 3, GREEN);
                break;

            case CMD_STOP:
                Serial.println("CMD_STOP");
                masterWantsData = false;
                M5.Display.fillCircle(5, 5, 3, RED);
                break;

            case CMD_SLEEP:
                Serial.println("CMD_STOP");
                // IL MASTER ORDINA IL SONNO
                goToLightSleep(); // Si addormenta su ordine del Master
                break;
        }
    }
}



void setup() {
    Serial.begin(115200);
    auto cfg = M5.config();
    cfg.internal_imu = true;
    M5.begin(cfg);
    M5.Imu.setCalibration(0, 0, 0);


    M5.Display.setBrightness(0);
    M5.Display.sleep(); 
    // Inizializza lo speaker
    M5.Speaker.setVolume(255); // Volume da 0 a 255
    M5.Speaker.tone(1000, 100);
    delay(150);
    M5.Speaker.tone(1500, 100);

    M5.Display.fillScreen(BLACK);
    M5.Display.drawString("SLAVE (L)", 10, 30);

    WiFi.mode(WIFI_STA);
    WiFi.disconnect();

    if (esp_now_init() == ESP_OK) {
        esp_now_register_recv_cb(esp_now_recv_cb_t(OnDataRecv));
    }

    esp_now_peer_info_t peerInfo = {};
    memcpy(peerInfo.peer_addr, masterAddress, 6);
    peerInfo.channel = 0;  
    peerInfo.encrypt = false;
    esp_now_add_peer(&peerInfo);
}

void loop() {
    M5.update();
    if (M5.BtnA.wasPressed()) {
        displayOn = !displayOn;
        if (displayOn) {
            M5.Display.setBrightness(10); 
            M5.Display.wakeup();
        } else {
            M5.Display.setBrightness(0);   // Spegni retroilluminazione
            M5.Display.sleep();            // Metti il display in basso consumo
        }
    }
    if (M5.BtnA.pressedFor(2000)) {
        M5.Speaker.tone(1500, 100);
        delay(150);
        M5.Speaker.tone(1000, 100);
        delay(100);
        goToLightSleep();
    }

    if (masterWantsData) {
        if (M5.Imu.update()) {
            M5.Imu.getAccel(&myData.accX, &myData.accY, &myData.accZ);
            M5.Imu.getGyro(&myData.gyroX, &myData.gyroY, &myData.gyroZ);
            if (myData.accX == lastCheckX) {
                stallCount++;
            } else {
                stallCount = 0;
                lastCheckX = myData.accX;
            }

            if (stallCount > 10) {
                M5.Imu.begin(); // Forza il re-init del sensore hardware
                M5.Imu.setCalibration(0,0,0);
                stallCount = 0;
            }

            esp_now_send(masterAddress, (uint8_t *) &myData, sizeof(myData));
        }
    }

    if (displayOn) {
        if(millis() - lastBatUpdate > 10000){
            int batPct = M5.Power.getBatteryLevel();
        
            // Puliamo solo l'area della scritta per non ridisegnare tutto lo schermo
            M5.Display.fillRect(10, 100, 120, 20, BLACK); 
            M5.Display.setCursor(10, 100);
            M5.Display.printf("BAT: %d%%", batPct);
            
            lastBatUpdate = millis();
        }
        
    }

    delay(20);
}