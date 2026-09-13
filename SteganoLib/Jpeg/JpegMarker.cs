namespace SteganoLib.Jpeg
{
    internal static class JpegMarker
    {
        public const byte Prefix = 0xFF;
        public const byte Soi = 0xD8;
        public const byte Eoi = 0xD9;
        public const byte Sos = 0xDA;
        public const byte Dqt = 0xDB;
        public const byte Dht = 0xC4;
        public const byte Dri = 0xDD;
        public const byte Dnl = 0xDC;
        public const byte Sof0 = 0xC0;
        public const byte Sof1 = 0xC1;
        public const byte Sof2 = 0xC2;
        public const byte Sof3 = 0xC3;
        public const byte Sof15 = 0xCF;
        public const byte Rst0 = 0xD0;
        public const byte Rst7 = 0xD7;
        public const byte App0 = 0xE0;
        public const byte App15 = 0xEF;
        public const byte Com = 0xFE;
        public const byte Stuffing = 0x00;

        public static bool IsRestart(byte marker) => marker >= Rst0 && marker <= Rst7;

        public static bool IsApp(byte marker) => marker >= App0 && marker <= App15;

        public static bool IsSof(byte marker) => marker >= Sof0 && marker <= Sof15 && marker != Dht && marker != 0xC8 && marker != 0xCC;
    }
}
