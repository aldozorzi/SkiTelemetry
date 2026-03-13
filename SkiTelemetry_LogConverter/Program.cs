using System.Globalization;

Console.WriteLine("--- Ski Telemetry Binary Converter ---");

// 1. Chiediamo il percorso del file .bin
Console.Write("Trascina qui il file .bin e premi Invio: ");
string? inputPath = Console.ReadLine()?.Replace("\"", ""); // Rimuove virgolette se trascinato

if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
{
    Console.WriteLine("Errore: File non trovato.");
    return;
}

string outputPath = Path.ChangeExtension(inputPath, ".csv");

try
{
    using (var reader = new BinaryReader(File.Open(inputPath, FileMode.Open)))
    using (var writer = new StreamWriter(outputPath))
    {
        // Intestazione CSV (usiamo il punto e virgola per Excel italiano)
        writer.WriteLine("Timestamp;Side;AccX;AccY;AccZ;GyroX;GyroY;GyroZ;RawRoll");

        int recordCount = 0;
        long fileLength = reader.BaseStream.Length;

        while (reader.BaseStream.Position < fileLength)
        {
            // Leggiamo i dati rispettando l'ordine di scrittura del TelemetryLogger
            uint ts = reader.ReadUInt32();
            byte side = reader.ReadByte();
            float ax = reader.ReadSingle();
            float ay = reader.ReadSingle();
            float az = reader.ReadSingle();
            float gx = reader.ReadSingle();
            float gy = reader.ReadSingle();
            float gz = reader.ReadSingle();
            //float roll = reader.ReadSingle();

            // Formattiamo i float con la virgola o il punto in base alla cultura 
            // ma forziamo l'invariante per non avere problemi con i software di analisi
            IFormatProvider provider = CultureInfo.InvariantCulture;

            writer.WriteLine($"{ts};{side};{ax.ToString(provider)};{ay.ToString(provider)};{az.ToString(provider)};" +
                             $"{gx.ToString(provider)};{gy.ToString(provider)};{gz.ToString(provider)};");

            recordCount++;
        }

        Console.WriteLine($"\nConversione completata!");
        Console.WriteLine($"Record processati: {recordCount}");
        Console.WriteLine($"File salvato: {outputPath}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\nErrore durante la conversione: {ex.Message}");
}

Console.WriteLine("\nPremi un tasto per uscire...");
Console.ReadKey();