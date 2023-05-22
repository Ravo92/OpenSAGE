﻿using System;

namespace OpenSage.Data.Ini
{
    internal readonly struct IniToken
    {
        public readonly ReadOnlyMemory<char> Memory;
        // TODO: this should not exist at all
        public readonly string Text => Memory.ToString();
        public readonly IniTokenPosition Position;

        public IniToken(ReadOnlyMemory<char> text, in IniTokenPosition position)
        {
            Memory = text;
            Position = position;
        }
    }
}
