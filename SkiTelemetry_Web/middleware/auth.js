const jwt = require('jsonwebtoken');

const authenticateToken = (req, res, next) => {
  // 1. Preleviamo l'header 'Authorization'
  const authHeader = req.headers['authorization'];
  
  // L'header di solito è "Bearer <token>", quindi dobbiamo splittarlo
  const token = authHeader && authHeader.split(' ')[1];

  if (!token) {
    return res.status(401).json({ error: "Access denied. No token provided." });
  }

  try {
    // 2. Verifichiamo il token usando la nostra chiave segreta
    const verified = jwt.verify(token, process.env.JWT_SECRET);
    
    // 3. Salviamo i dati dell'utente (id, ruolo) nell'oggetto 'req'
    // Così le rotte successive sapranno CHI sta facendo la richiesta
    req.user = verified;
    
    // 4. Tutto ok, passiamo alla rotta vera e propria
    next();
  } catch (err) {
    res.status(403).json({ error: "Invalid or expired token." });
  }
};

// FONDAMENTALE: Esporta la funzione
module.exports = authenticateToken;