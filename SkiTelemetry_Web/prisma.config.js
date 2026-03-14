require('dotenv').config();
module.exports = {
  datasource: {
    url: process.env.DB_DATABASE_URL
  },
};