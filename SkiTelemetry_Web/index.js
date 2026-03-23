require('dotenv').config();

const prisma = require('./lib/prisma');
const jwt = require('jsonwebtoken');
const express = require('express');
const bcrypt = require('bcryptjs');
const authenticateToken = require('./middleware/auth');
const app = express();
const { S3Client, PutObjectCommand,GetObjectCommand,DeleteObjectCommand } = require("@aws-sdk/client-s3");
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
  forcePathStyle: true,
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

  await prisma.refreshToken.upsert({
    where: {
      userId: user.id, 
    },
    update: {
      token: refreshToken,
      createdAt: new Date(), // Aggiorniamo anche la data per sapere quando è stato emesso l'ultimo
    },
    create: {
      token: refreshToken,
      userId: user.id,
    },
  });

  return { accessToken, refreshToken };
}

async function deleteBucketFile(s3Key){
  try {
      const deleteCommand = new DeleteObjectCommand({
        Bucket: process.env.R2_BUCKET_NAME,
        Key: s3Key,
      });
      
      await s3.send(deleteCommand);
      console.log(`Successfully deleted file from R2: ${s3Key}`);
    } catch (s3Error) {
      // Se fallisce anche l'eliminazione, logghiamo l'errore critico
      console.error("CRITICAL: Failed to delete orphaned file from R2:", s3Error);
    }
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
  const { email, password, role, firstName, lastName } = req.body || {};

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
      data: { email, password: hashedPassword, role, firstName, lastName },
    });

    // AUTO-LOGIN: Generiamo i token subito
    const { accessToken, refreshToken } = await generateAndSaveTokens(user);

    res.status(201).json({ 
      message: "User created and logged in", 
      accessToken, 
      refreshToken,
      user: { id: user.id, role: user.role, email: user.email, firstName: user.firstName, lastName:user.lastName }
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
      user: { id: user.id, role: user.role, email: user.email, firstName: user.firstName, lastName:user.lastName }
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

    res.json(
      { accessToken: newAccessToken }
    );

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
  const { s3Key, fileName, notes } = req.body;

  if (!s3Key || !fileName) {
    return res.status(400).json({ error: "Missing required metadata" });
  }

  try {
    // Solo ora, con il file già su R2, scriviamo su Neon
    //console.log(`Trying to save to database - s3Key: ${s3Key}, fileName:${fileName}, userId:${req.user.userId}`);
    const newRun = await prisma.run.create({
      data: {
        s3Key: s3Key,
        fileName: fileName,
        notes: notes || null, // Salviamo le note se presenti
        userId: req.user.userId
      }
    });
    res.status(201).json({ message: "Run saved successfully", run: newRun });
  } catch (error) {
    console.error(error);
    await deleteBucketFile(s3Key);
    res.status(500).json({ error: "Failed to save run to database" });
  }
});
app.patch('/api/run/:runId/notes', authenticateToken, async (req, res) => {
  const { runId } = req.params;
  const { notes } = req.body;
  const userId = req.user.userId;

  try {
    // 1. Recuperiamo la run per controllare i permessi
    const run = await prisma.run.findUnique({
      where: { id: runId }
    });

    if (!run) return res.status(404).json({ error: "Run non trovata" });

    // 2. Controllo Permessi:
    // Può modificare le note solo il proprietario (Atleta) 
    // O un coach che ha una relazione ACCEPTED con quell'atleta
    const isOwner = run.userId === userId;

    if (!isOwner) {
      return res.status(403).json({ error: "Non hai i permessi per modificare queste note" });
    }
    //console.log(`[/api/run/:runId/notes] Updating Run: ${runId}`);
    // 3. Aggiornamento su Prisma
    const updatedRun = await prisma.run.update({
      where: { id: runId },
      data: { notes: notes }
    });

    res.json({ message: "Note aggiornate correttamente", notes: updatedRun.notes });

  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Errore durante l'aggiornamento delle note" });
  }
});

app.get('/api/run/mine', authenticateToken, async (req, res) => {
  const page = parseInt(req.query.page) || 1;
  const limit = parseInt(req.query.limit) || 20;
  const skip = (page - 1) * limit;
  try {
    const runs = await prisma.run.findMany({
      where: {
        userId: req.user.userId, // Filtriamo per l'utente loggato
      },
      orderBy: {
        fileName: 'desc', // Ordine decrescente (la più recente per prima)
      },
      // Opzionale: possiamo scegliere quali campi restituire
      select: {
        id: true,
        fileName: true,
        s3Key: true,
        createdAt: true,
        notes: true
      },
      take: limit, // Prendi solo X record
      skip: skip,  // Salta i precedenti
    });

    const totalRuns = await prisma.run.count({
      where: { userId: req.user.userId }
    });

    res.json({
      metadata: {
        total: totalRuns,
        page: page,
        totalPages: Math.ceil(totalRuns / limit)
      },
      runs: runs
    });
  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Errore nel recupero delle tue sciate" });
  }
});
app.delete('/api/run/delete/:runId', authenticateToken, async (req, res) => {
  const { runId } = req.params;
  // console.log(`[/api/run/delete] Trying to get run ${runId}`)
  const userId = req.user.userId;

  if (!runId) {
    return res.status(400).json({ error: "Missing param." });
  }

  try {
    // 1. Recuperiamo l'invito
    const run = await prisma.run.findUnique({
      where: { id: runId }
    });
    
    if (!run) {
      return res.status(404).json({ error: `Run not found:${runId}` });
    }

    // 2. Controllo Sicurezza: 
    if (run.userId !== userId) {
      return res.status(403).json({ error: "Unauthorized to delete this connection." });
    }
    
    // 3. Cancellazione fisica
    await prisma.run.delete({
      where: { id: runId }
    });
    await deleteBucketFile(run.s3Key);
    res.json({ message: "Run removed successfully." });

  } catch (error) {
    console.error("Delete Run Error:", error);
    res.status(500).json({ error: "Internal Server Error." });
  }
});

app.post('/api/run/get-download-url', authenticateToken, async (req, res) => {
  const { s3Key } = req.body; // Passiamo lo s3Key che abbiamo salvato nel DB

  if (!s3Key) return res.status(400).json({ error: "s3Key is required" });

  try {
    const command = new GetObjectCommand({
      Bucket: process.env.R2_BUCKET_NAME,
      Key: s3Key,
    });

    // Genera l'URL per il download (scadenza 1 ora)
    const downloadUrl = await getSignedUrl(s3, command, { expiresIn: 3600 });

    res.json({ downloadUrl });
  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Could not generate download URL" });
  }
});
app.post('/api/coach/invite-athlete', authenticateToken, async (req, res) => {
  const { athleteId } = req.body;
  const coachId = req.user.userId; // Preso dal token JWT
  const coachRole = req.user.role;

  // 1. Controllo permessi: solo i Coach possono invitare
  if (coachRole !== 'COACH') {
    return res.status(403).json({ error: "Only coaches can invite athletes." });
  }

  if (!athleteId) {
    return res.status(400).json({ error: "Missing Param." });
  }

  try {
    // 2. Cerchiamo l'atleta nel database tramite email
    const athlete = await prisma.user.findUnique({
      where: { id: athleteId}
    });

    if (!athlete) {
      return res.status(404).json({ error: "Athlete not found." });
    }
    if (athleteId === coachId) {
      return res.status(400).json({ error: "You cannot invite yourself." });
    }

    // 3. Creazione del collegamento (Invito)
    // Usiamo connectOrCreate o un semplice create gestendo l'errore di duplicato
    const invitation = await prisma.coachAthlete.create({
      data: {
        coachId: coachId,
        athleteId: athlete.id,
        invitedBy: coachId,
        status: "PENDING" // L'atleta dovrà poi accettare (opzionale)
      }
    });

    res.status(201).json({ 
      message: "Invito inviato con successo!", 
      athleteName: `${athlete.firstName} ${athlete.lastName}` 
    });

  } catch (error) {
    // Gestione errore record duplicato (se il coach lo ha già invitato)
    if (error.code === 'P2002') {
      return res.status(400).json({ error: "Athlete already invited." });
    }
    console.error(error);
    res.status(500).json({ error: "Internal Server Error." });
  }
});
app.get('/api/coach/list-athletes', authenticateToken, async (req, res) => {
  const coachId = req.user.userId;
  const coachRole = req.user.role;

  // Sicurezza: solo i coach possono vedere questa lista
  if (coachRole !== 'COACH') {
    return res.status(403).json({ error: "Only coaches can access this list." });
  }

  try {
    const invitations = await prisma.coachAthlete.findMany({
      where: {
        coachId: coachId,
        // Qui mostriamo tutto: PENDING, ACCEPTED, REJECTED
      },
      include: {
        athlete: {
          select: {
            id: true,
            firstName: true,
            lastName: true,
            email: true
          }
        }
      },
      orderBy: {
        createdAt: 'desc'
      }
    });

    // Formattiamo per il frontend
    const formatted = invitations.map(inv => ({
      invitationId: inv.id,
      invitationStatus: inv.status,
      athleteId: inv.athlete.id,
      athleteName: `${inv.athlete.firstName} ${inv.athlete.lastName}`,
      athleteEmail: inv.athlete.email,
      date: inv.createdAt
    }));

    res.json(formatted);

  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Internal Server Error." });
  }
});
app.get('/api/coach/athletes-runs', authenticateToken, async (req, res) => {

  const page = parseInt(req.query.page) || 1;
  const limit = parseInt(req.query.limit) || 20;
  const skip = (page - 1) * limit;

  const coachId = req.user.userId;
  const coachRole = req.user.role;

  if (coachRole !== 'COACH') {
    return res.status(403).json({ error: "Access denied. Only coaches can view athletes' runs." });
  }

  try {
    // 1. Troviamo gli ID di tutti gli atleti che hanno accettato questo coach
    const acceptedConnections = await prisma.coachAthlete.findMany({
      where: {
        coachId: coachId,
        status: 'ACCEPTED'
      },
      select: { athleteId: true }
    });

    const athleteIds = acceptedConnections.map(c => c.athleteId);

    if (athleteIds.length === 0) {
      return res.json([]); // Nessun atleta connesso, nessuna run da mostrare
    }

    // 2. Recuperiamo le run di quegli atleti
    const runs = await prisma.run.findMany({
      where: {
        userId: { in: athleteIds }
      },
      include: {
        owner: {
          select: {
            firstName: true,
            lastName: true,
            email: true
          }
        }
      },
      orderBy: {
        fileName: 'desc'
      },
      take: limit, // Prendi solo X record
      skip: skip,  // Salta i precedenti
    });

    const totalRuns = await prisma.run.count({
      where: { userId: { in: athleteIds } }
    });

    // 3. Formattiamo per il frontend
    const formattedRuns = runs.map(run => ({
      runId: run.id,
      fileName: run.fileName,
      s3Key: run.s3Key,
      notes: run.notes,
      createdAt: run.createdAt,
      athleteId: run.userId,
      athleteName: `${run.owner.firstName} ${run.owner.lastName}`,
      athleteEmail: run.owner.email
    }));

    res.json({
      metadata: {
        total: totalRuns,
        page: page,
        totalPages: Math.ceil(totalRuns / limit)
      },
      runs: formattedRuns
    });

  } catch (error) {
    console.error("Error fetching athletes' runs:", error);
    res.status(500).json({ error: "Internal Server Error." });
  }
});
app.put('/api/athlete/accept-coach', authenticateToken, async (req, res) => {
  const { invitationId } = req.body;
  if (!invitationId) {
    return res.status(400).json({ error: "Missing Param." });
  }
  const athleteId = req.user.userId;

  try {
    // Cerchiamo l'invito specifico tra questo coach e questo atleta
    const invitation = await prisma.coachAthlete.findUnique({
      where: { id: invitationId }
    });

    if (!invitation) {
      return res.status(404).json({ error: "Invitation not found." });
    }
    if (invitation.athleteId !== athleteId) {
      return res.status(403).json({ error: "You are not authorized to accept this invitation." });
    }
    if (invitation.status == 'ACCEPTED') {
      return res.status(400).json({ error: "Invitation already accepted." });
    }
    if (invitation.invitedBy === req.user.userId) {
      return res.status(403).json({ error: "You cannot accept your own invitation." });
    }

    // Aggiorniamo lo stato
    const updatedInvitation = await prisma.coachAthlete.update({
      where: {
        id: invitationId
      },
      data: {
        status: 'ACCEPTED'
      }
    });

    res.json({ message: "Invitation accepted!", status: updatedInvitation.status });

  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Internal Server Error." });
  }
});
app.put('/api/athlete/reject-coach', authenticateToken, async (req, res) => {
  const { invitationId } = req.body;
  if (!invitationId) {
    return res.status(400).json({ error: "Missing Param." });
  }
  const athleteId = req.user.userId;

  try {
    // Cerchiamo l'invito specifico tra questo coach e questo atleta
    const invitation = await prisma.coachAthlete.findUnique({
      where: { id: invitationId }
    });

    if (!invitation) {
      return res.status(404).json({ error: "Invitation not found." });
    }
    if (invitation.athleteId !== athleteId) {
      return res.status(403).json({ error: "You are not authorized to reject this invitation." });
    }
    if (invitation.status == 'REJECTED') {
      return res.status(400).json({ error: "Invitation already rejected." });
    }
    if (invitation.invitedBy === req.user.userId) {
      return res.status(403).json({ error: "You cannot reject your own invitation." });
    }

    // Aggiorniamo lo stato
    const updatedInvitation = await prisma.coachAthlete.update({
      where: {
        id: invitationId
      },
      data: {
        status: 'REJECTED'
      }
    });

    res.json({ message: "Invitation rejected!", status: updatedInvitation.status });

  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Internal Server Error." });
  }
});
app.get('/api/athlete/list-coaches', authenticateToken, async (req, res) => {
  const athleteId = req.user.userId;

  try {
    const pendingInvitations = await prisma.coachAthlete.findMany({
      where: {
        athleteId: athleteId,
        // Sicurezza extra: l'invito NON deve essere stato creato dall'atleta stesso
        invitedBy: { not: athleteId } 
      },
      include: {
        coach: {
          select: {
            id: true,
            firstName: true,
            lastName: true,
            email: true
          }
        }
      }
    });

    // Formattiamo la risposta per renderla facile da leggere per il frontend MAUI
    const formattedInvitations = pendingInvitations.map(inv => ({
      invitationId: inv.id,
      invitationStatus: inv.status,
      coachId: inv.coach.id,
      coachName: `${inv.coach.firstName} ${inv.coach.lastName}`,
      coachEmail: inv.coach.email,
      date: inv.createdAt
    }));

    res.json(formattedInvitations);

  } catch (error) {
    console.error(error);
    res.status(500).json({ error: "Internal Server Error." });
  }
});
app.delete('/api/connection/delete:connectionId', authenticateToken, async (req, res) => {
  const { connectionId } = req.params;
  const userId = req.user.userId;

  if (!connectionId) {
    return res.status(400).json({ error: "Missing param." });
  }

  try {
    // 1. Recuperiamo l'invito
    const invitation = await prisma.coachAthlete.findUnique({
      where: { id: connectionId }
    });

    if (!invitation) {
      return res.status(404).json({ error: "Connection not found." });
    }

    // 2. Controllo Sicurezza: 
    if (invitation.invitedBy !== userId) {
      return res.status(403).json({ error: "Unauthorized to delete this connection." });
    }

    // 3. Cancellazione fisica
    await prisma.coachAthlete.delete({
      where: { id: connectionId }
    });

    res.json({ message: "Connection removed successfully." });

  } catch (error) {
    console.error("Delete Connection Error:", error);
    res.status(500).json({ error: "Internal Server Error." });
  }
});

app.listen(PORT, () => {
  console.log(`Server in esecuzione su http://localhost:${PORT}`);
});

module.exports = app; // Fondamentale per Vercel