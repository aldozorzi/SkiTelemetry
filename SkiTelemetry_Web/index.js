const express = require('express');
const app = express();
const PORT = process.env.PORT || 3000;

app.use(express.json());

// Rotta di test per vedere se l'API risponde
app.get('/', (req, res) => {
  res.json({ 
    status: "Online", 
    message: "Backend SkiTelemetry API pronto per Vercel!",
    timestamp: new Date().toISOString()
  });
});

// Simulazione rotta di registrazione
app.post('/api/auth/signup', (req, res) => {
    const { email, role } = req.body;
    res.json({
        message: `Utente ${email} ricevuto come ${role}. (Simulazione: DB non ancora collegato)`,
        success: true
    });
});

app.listen(PORT, () => {
  console.log(`Server in esecuzione su http://localhost:${PORT}`);
});

module.exports = app; // Fondamentale per Vercel