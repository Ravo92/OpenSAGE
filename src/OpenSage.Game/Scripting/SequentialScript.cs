﻿namespace OpenSage.Scripting
{
    internal sealed class SequentialScript
    {
        public uint Unknown1;
        public uint TeamID;
        public string ScriptName;
        public uint ScriptActionIndex;
        public uint LoopsRemaining;
        public int Unknown2 = -1;

        public void Load(SaveFileReader reader)
        {
            reader.ReadVersion(1);

            Unknown1 = reader.ReadUInt32();

            TeamID = reader.ReadUInt32();
            ScriptName = reader.ReadAsciiString();
            ScriptActionIndex = reader.ReadUInt32();
            LoopsRemaining = reader.ReadUInt32();

            reader.ReadInt32(ref Unknown2);
            if (Unknown2 != -1)
            {
                throw new InvalidStateException();
            }

            reader.SkipUnknownBytes(1);
        }
    }
}
