/*
  Warnings:

  - Added the required column `invitedBy` to the `CoachAthlete` table without a default value. This is not possible if the table is not empty.

*/
-- AlterTable
ALTER TABLE "CoachAthlete" ADD COLUMN     "invitedBy" TEXT NOT NULL;
