const { Pool } = require('pg'); // Assicurati che ci siano le parentesi graffe
const { PrismaPg } = require('@prisma/adapter-pg');
const { PrismaClient } = require('@prisma/client');

// Creiamo il pool di connessione standard di Postgres
const pool = new Pool({ connectionString: process.env.DB_DATABASE_URL });

// Creiamo l'adapter che fa da ponte tra Prisma e il driver Postgres
const adapter = new PrismaPg(pool);

// Passiamo l'adapter al costruttore come richiesto
const prisma = new PrismaClient({ adapter });

module.exports = prisma;