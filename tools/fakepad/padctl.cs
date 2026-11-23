// Keeps the "Local\CoCHD_FakePad" XINPUT_STATE alive and copies pad.txt into it every 5 ms.
// pad.txt: "<buttons hex> <lx> <ly> <rx> <ry> <lt> <rt>" (sticks -32768..32767, triggers 0..255); "quit" ends.
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

static class PadCtl
{
    static void Main(string[] args)
    {
        var file = args[0];
        using (var mm = MemoryMappedFile.CreateNew(@"Local\CoCHD_FakePad", 16))
        using (var view = mm.CreateViewAccessor())
        {
            uint packet = 0;
            string last = null;
            for (;;)
            {
                string line;
                try { line = File.ReadAllText(file).Trim(); } catch (IOException) { line = last; }
                if (line == "quit")
                    return;
                if (line != null && line != last)
                {
                    var p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length == 7)
                    {
                        packet++;
                        view.Write(0, packet);
                        view.Write(4, Convert.ToUInt16(p[0], 16));
                        view.Write(6, byte.Parse(p[5]));
                        view.Write(7, byte.Parse(p[6]));
                        view.Write(8, short.Parse(p[1]));
                        view.Write(10, short.Parse(p[2]));
                        view.Write(12, short.Parse(p[3]));
                        view.Write(14, short.Parse(p[4]));
                    }
                    last = line;
                }
                Thread.Sleep(5);
            }
        }
    }
}
