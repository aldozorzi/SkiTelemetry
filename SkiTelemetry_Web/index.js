require('dotenv').config();

const prisma = require('./lib/prisma');
const jwt = require('jsonwebtoken');
const express = require('express');
const bcrypt = require('bcryptjs');
const authenticateToken = require('./middleware/auth');
const app = express();
const { S3Client, PutObjectCommand } = require("@aws-sdk/client-s3");
const { getSignedUrl } = require("@aws-sdk/s3-request-presigner");
const crypto = require('crypto');

const PORT = process.env.PORT || 3000;
const s3 = new S3Client({
  region: "auto",
  endpoint: process.env.R2_ENDPOINT,
  credentials: {
    accessKeyId: process.env.R2_ACCESS_KEY_ID,
    secretAccessKey: process.env.R2_SECRET_ACCESS_KEY,
  },
});

// --- FUNZIONE DI UTILITY (da mettere sopra le rotte) ---
async function generateAndSaveTokens(user) {
  // 1. Access Token (JWT)
  const accessToken = jwt.sign(
    { userId: user.id, email: user.email, role: user.role },
    process.env.JWT_SECRET,
    { expiresIn: '1h' }
  );

  // 2. Refresh Token (Stringa casuale)
  const refreshToken = crypto.randomBytes(64).toString('hex');

  // 3. Salvataggio su DB
  await prisma.refreshToken.create({
    data: {
      token: refreshToken,
      userId: user.id
    }
  });

  return { accessToken, refreshToken };
}


app.use(express.json());

// Rotta di test per vedere se l'API risponde
app.get('/', (req, res) => {
  res.json({ 
    status: "Online", 
    message: "Backend SkiTelemetry API pronto per Vercel!",
    timestamp: new Date().toISOString()
  });
});

//  rotta di registrazione
app.post('/api/auth/signup', async (req, res) => {
  // 1. Controllo se il body esiste e se ha i campi necessari
  const { email, password, role } = req.body || {};

  if (!email || !password || !role) {
    return res.status(400).json({ 
      error: "Missing required fields"
    });
  }

  // 2. Controllo se il ruolo è valido (solo COACH o ATHLETE come da schema Prisma)
  const validRoles = ['COACH', 'ATHLETE'];
  if (!validRoles.includes(role)) {
    return res.status(400).json({ error: `Invalid role. Sent: ${role}` });
  }

  try {
    const hashedPassword = await bcrypt.hash(password, 10);
    const user = await prisma.user.create({
      data: { email, password: hashedPassword, role },
    });

    // AUTO-LOGIN: Generiamo i token subito
    const { accessToken, refreshToken } = await generateAndSaveTokens(user);

    res.status(201).json({ 
      message: "User created and logged in", 
      accessToken, 
      refreshToken,
      user: { id: user.id, role: user.role, email: user.email }
    });
  } catch (error) {
    if (error.code === 'P2002') {
      return res.status(400).json({ error: "Email already exists" });
    }
    console.error("Signup Error:", error);
    res.status(500).json({ error: "Internal Server Error" });
  }
});

app.post('/api/auth/login', async (req, res) => {
  const { email, password } = req.body || {};

  // 1. Validazione input
  if (!email || !password) {
    return res.status(400).json({ error: "Email and password are required" });
  }

  try {
    // 2. Cerchiamo l'utente nel DB tramite email
    const user = await prisma.user.findUnique({
      where: { email }
    });

    // Se l'utente non esiste
    if (!user) {
      return res.status(401).json({ error: "Invalid credentials" });
    }

    // 3. Confrontiamo la password inviata con quella criptata nel DB
    const isPasswordValid = await bcrypt.compare(password, user.password);

    if (!isPasswordValid) {
      return res.status(401).json({ error: "Invalid credentials" });
    }

    // 4. Se la password è valida, creiamo il TOKEN
    // 1. Genera Access Token (scadenza breve, es. 1 ora)
    const { accessToken, refreshToken } = await generateAndSaveTokens(user);

    // 4. Rispondi all'app con entrambi
    res.status(200).json({
      message: "Login successful",
      accessToken,
      refreshToken,
      user: { id: user.id, role: user.role, email: user.email }
    });

  } catch (error) {
    console.error("Login Error:", error);
    res.status(500).json({ error: "Internal Server Error" });
  }
});

app.get('/api/auth/profile', authenticateToken, (req, res) => {
  // 'req.user' è stato riempito dal middleware sopra
  res.json({
    message: "Welcome to your protected profile",
    user: req.user // Qui vedrai userId, email e role estratti dal JWT
  });
});

app.post('/api/auth/refresh', async (req, res) => {
  const { refreshToken } = req.body;

  if (!refreshToken) {
    return res.status(401).json({ error: "Refresh Token richiesto" });
  }

  try {
    // 1. Cerca il token nel DB e includi i dati dell'utente
    const savedToken = await prisma.refreshToken.findUnique({
      where: { token: refreshToken },
      include: { user: true }
    });

    if (!savedToken) {
      return res.status(403).json({ error: "Refresh Token non valido" });
    }

    // 2. Se esiste, genera un nuovo Access Token
    const newAccessToken = jwt.sign(
      { userId: savedToken.user.id, email: savedToken.user.email, role: savedToken.user.role },
      process.env.JWT_SECRET,
      { expiresIn: '1h' }
    );

    res.json({ accessToken: newAccessToken });

  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Errore durante il refresh del token" });
  }
});

app.post('/api/run/get-upload-url', authenticateToken, async (req, res) => {
  const { fileName, contentType } = req.body; // Es: "sciata.csv", "text/csv"

  if (!fileName) return res.status(400).json({ error: "File name is required" });

  // Creiamo una chiave univoca per S3 (evita sovrascritture se due utenti caricano un file col lo stesso nome)
  const folder = `runs/${req.user.userId}`;
  const s3FileName = `${Date.now()}-${fileName}`;
  const s3Key = `${folder}/${s3FileName}`;

  try {
    const command = new PutObjectCommand({
      Bucket: process.env.R2_BUCKET_NAME,
      Key: s3Key,
      ContentType: contentType,
    });

    // Genera l'URL magico che scade tra 3600 secondi (1 ora)
    const uploadUrl = await getSignedUrl(s3, command, { expiresIn: 3600 });
    

    res.json({ 
      uploadUrl, 
      s3Key,      // Il percorso completo per R2 e per il DB
      fileName,   // Il nome originale "pulito" (es. discesa.csv)
      s3FileName  // Il nome del file così come salvato (es. 12345-discesa.csv)
    });
  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Could not generate upload URL" });
  }
});

app.post('/api/run/save', authenticateToken, async (req, res) => {
  const { s3Key, fileName } = req.body;

  if (!s3Key || !fileName) {
    return res.status(400).json({ error: "Missing required metadata" });
  }

  try {
    // Solo ora, con il file già su R2, scriviamo su Neon
    const newRun = await prisma.run.create({
      data: {
        s3Key: s3Key,
        fileName: fileName,
        userId: req.user.userId
      }
    });

    res.status(201).json({ message: "Run saved successfully", run: newRun });
  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Failed to save run to database" });
  }
});

app.get('/api/run/mine', authenticateToken, async (req, res) => {
  try {
    const runs = await prisma.run.findMany({
      where: {
        userId: req.user.userId, // Filtriamo per l'utente loggato
      },
      orderBy: {
        createdAt: 'desc', // Ordine decrescente (la più recente per prima)
      },
      // Opzionale: possiamo scegliere quali campi restituire
      select: {
        id: true,
        fileName: true,
        s3Key: true,
        createdAt: true,
      }
    });

    res.json(runs);
  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Errore nel recupero delle tue sciate" });
  }
});

app.listen(PORT, () => {
  console.log(`Server in esecuzione su http://localhost:${PORT}`);
});

module.exports = app; // Fondamentale per Vercel