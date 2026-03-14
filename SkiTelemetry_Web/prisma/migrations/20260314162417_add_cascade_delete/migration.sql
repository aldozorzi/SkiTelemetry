-- DropForeignKey
ALTER TABLE "CoachAthlete" DROP CONSTRAINT "CoachAthlete_athleteId_fkey";

-- DropForeignKey
ALTER TABLE "CoachAthlete" DROP CONSTRAINT "CoachAthlete_coachId_fkey";

-- DropForeignKey
ALTER TABLE "RefreshToken" DROP CONSTRAINT "RefreshToken_userId_fkey";

-- DropForeignKey
ALTER TABLE "Run" DROP CONSTRAINT "Run_userId_fkey";

-- AddForeignKey
ALTER TABLE "CoachAthlete" ADD CONSTRAINT "CoachAthlete_coachId_fkey" FOREIGN KEY ("coachId") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "CoachAthlete" ADD CONSTRAINT "CoachAthlete_athleteId_fkey" FOREIGN KEY ("athleteId") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Run" ADD CONSTRAINT "Run_userId_fkey" FOREIGN KEY ("userId") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "RefreshToken" ADD CONSTRAINT "RefreshToken_userId_fkey" FOREIGN KEY ("userId") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;
