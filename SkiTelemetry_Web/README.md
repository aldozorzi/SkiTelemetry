⛷️ SkiTelemetry API Documentation
Base URL: https://tua-api-su-vercel.app

Auth Strategy: Bearer Token (JWT) da inserire nell'header Authorization.

🔐 Autenticazione
Registrazione Utente
Endpoint: /api/auth/signup

Metodo: POST

Body (JSON):

JSON
{
  "email": "atleta@test.it",
  "password": "password123",
  "role": "ATHLETE" // o "COACH"
}
Login
Endpoint: /api/auth/login

Metodo: POST

Body (JSON): { "email": "...", "password": "..." }

Risposta: Restituisce il token da salvare nel SecureStorage della tua app MAUI.

Perfetto, se hai configurato lo script su pm.globals o pm.environment, l'importante è che il flusso sia fluido. Il fatto che il refresh funzioni significa che il tuo database, il Prisma Client e la logica JWT sono perfettamente sincronizzati.

Ecco il Markdown aggiornato da aggiungere alla tua documentazione tecnica per la rotta di Refresh.

🔄 Refresh Token
Aggiornamento Access Token
Endpoint: /api/auth/refresh

Metodo: POST

Auth: Nessuna (si usa il Refresh Token nel body)

Descrizione: Permette di ottenere un nuovo accessToken valido senza richiedere all'utente di inserire nuovamente le credenziali. Da chiamare quando le altre API restituiscono un errore 401 Unauthorized.

Request Body (JSON):

JSON
{
  "refreshToken": "stringa_esadecimale_molto_lunga_salvata_al_login"
}
Response (200 OK):

JSON
{
  "accessToken": "nuovo_jwt_valido_per_un_ora"
}
Errori comuni:

401 Unauthorized: Refresh Token mancante nel body.

403 Forbidden: Refresh Token non trovato nel database o non valido. -> forzare nuovo login

500 Internal Server Error: Problema di connessione al database.

🏁 Gestione Run (Telemetria)
1. Richiesta URL di Caricamento
Endpoint: /api/run/get-upload-url

Metodo: POST

Auth: Richiesto (Bearer Token)

Body (JSON): { "fileName": "discesa.csv", "contentType": "text/csv" }

Risposta:

JSON
{
  "uploadUrl": "https://...", // URL per Cloudflare R2
  "s3Key": "runs/id-utente/timestamp-discesa.csv",
  "fileName": "discesa.csv"
}
2. Upload del File (Direct to R2)
Destinazione: uploadUrl ricevuto sopra.

Metodo: PUT

Body: File binario (Stream).

Header: Content-Type deve corrispondere a quello dichiarato.

3. Conferma e Salvataggio DB
Endpoint: /api/run/save

Metodo: POST

Auth: Richiesto

Body (JSON): Inviare l'intero oggetto ricevuto al punto 1 (serve s3Key e fileName).

4. Lista Run Utente
Endpoint: /api/run/mine

Metodo: GET

Auth: Richiesto

Descrizione: Ritorna l'elenco delle sciate dell'utente loggato in ordine cronologico decrescente.