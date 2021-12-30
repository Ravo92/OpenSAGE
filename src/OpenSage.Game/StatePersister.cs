﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using OpenSage.Content;
using OpenSage.FileFormats;
using OpenSage.Graphics.ParticleSystems;
using OpenSage.Mathematics;

namespace OpenSage
{
    public abstract class StatePersister
    {
        protected readonly Stack<Segment> Segments;

        public readonly SageGame SageGame;
        public readonly AssetStore AssetStore;

        protected StatePersister(Game game)
        {
            Segments = new Stack<Segment>();

            SageGame = game.SageGame;
            AssetStore = game.AssetStore;
        }

        public abstract byte PersistVersion(byte maximumVersion);

        public abstract void PersistByte(ref byte value);

        public abstract void PersistInt16(ref short value);

        public abstract void PersistUInt16(ref ushort value);

        public abstract void PersistInt32(ref int value);

        public abstract void PersistUInt32(ref uint value);

        public abstract void PersistBoolean(ref bool value);

        public abstract void PersistObjectID(ref uint value);

        public abstract void PersistFrame(ref uint value);

        public abstract void PersistAsciiString(ref string value);

        public abstract void PersistUnicodeString(ref string value);

        public abstract void PersistSingle(ref float value);

        public abstract void PersistVector3(ref Vector3 value);

        public abstract void PersistPoint2D(ref Point2D value);

        public abstract void PersistPoint3D(ref Point3D value);

        public abstract void PersistEnum<TEnum>(ref TEnum value)
            where TEnum : struct;

        public abstract void PersistEnumByte<TEnum>(ref TEnum value)
            where TEnum : struct;

        public abstract void PersistEnumFlags<TEnum>(ref TEnum value)
            where TEnum : struct;

        public abstract void PersistEnumByteFlags<TEnum>(ref TEnum value)
            where TEnum : struct;

        public abstract void PersistMatrix4x3(ref Matrix4x3 value, bool readVersion = true);

        public abstract void PersistMatrix4x3Transposed(ref Matrix4x3 value);

        public abstract void PersistBitArray<TEnum>(ref BitArray<TEnum> result)
            where TEnum : Enum;

        public abstract void PersistColorRgba(ref ColorRgba value);

        public abstract void PersistColorRgbaInt(ref ColorRgba value);

        public abstract void PersistDateTime(ref DateTime value);

        public abstract void PersistRandomVariable(ref RandomVariable value);

        public abstract void PersistRandomAlphaKeyframe(ref RandomAlphaKeyframe value);

        public abstract void PersistRgbColorKeyframe(ref RgbColorKeyframe value);

        public abstract void PersistObjectNameAndIdSet(List<ObjectNameAndId> set);

        public abstract void ReadBytesIntoStream(Stream destination, int numBytes);

        public abstract uint BeginSegment(string segmentName);

        public abstract void EndSegment();

        protected record struct Segment(long Start, long End, string Name);

        public abstract void SkipUnknownBytes(int numBytes);
    }

    public sealed class StateReader : StatePersister
    {
        private readonly BinaryReader _binaryReader;

        internal StateReader(BinaryReader binaryReader, Game game)
            : base(game)
        {
            _binaryReader = binaryReader;
        }

        public override byte PersistVersion(byte maximumVersion)
        {
            var result = _binaryReader.ReadByte();
            if (result == 0 || result > maximumVersion)
            {
                throw new InvalidStateException();
            }
            return result;
        }

        public override void PersistByte(ref byte value) => value = _binaryReader.ReadByte();

        public override void PersistInt16(ref short value) => value = _binaryReader.ReadInt16();

        public override void PersistUInt16(ref ushort value) => value = _binaryReader.ReadUInt16();

        public override void PersistInt32(ref int value) => value = _binaryReader.ReadInt32();

        public override void PersistUInt32(ref uint value) => value = _binaryReader.ReadUInt32();

        public override void PersistBoolean(ref bool value) => value = _binaryReader.ReadBooleanChecked();

        public override void PersistObjectID(ref uint value) => value = _binaryReader.ReadUInt32();

        public override void PersistFrame(ref uint value) => value = _binaryReader.ReadUInt32();

        public override void PersistAsciiString(ref string value) => value = _binaryReader.ReadBytePrefixedAsciiString();

        public override void PersistUnicodeString(ref string value) => value = _binaryReader.ReadBytePrefixedUnicodeString();

        public override void PersistSingle(ref float value) => value = _binaryReader.ReadSingle();

        public override void PersistVector3(ref Vector3 value) => value = _binaryReader.ReadVector3();

        public override void PersistPoint2D(ref Point2D value) => value = _binaryReader.ReadPoint2D();

        public override void PersistPoint3D(ref Point3D value) => value = _binaryReader.ReadPoint3D();

        public override void PersistEnum<TEnum>(ref TEnum value) => value = _binaryReader.ReadUInt32AsEnum<TEnum>();

        public override void PersistEnumByte<TEnum>(ref TEnum value) => value = _binaryReader.ReadByteAsEnum<TEnum>();

        public override void PersistEnumFlags<TEnum>(ref TEnum value) => value = _binaryReader.ReadUInt32AsEnumFlags<TEnum>();

        public override void PersistEnumByteFlags<TEnum>(ref TEnum value) => value = _binaryReader.ReadByteAsEnumFlags<TEnum>();

        public override void PersistMatrix4x3(ref Matrix4x3 value, bool readVersion)
        {
            if (readVersion)
            {
                PersistVersion(1);
            }

            var m11 = _binaryReader.ReadSingle();
            var m21 = _binaryReader.ReadSingle();
            var m31 = _binaryReader.ReadSingle();
            var m41 = _binaryReader.ReadSingle();

            var m12 = _binaryReader.ReadSingle();
            var m22 = _binaryReader.ReadSingle();
            var m32 = _binaryReader.ReadSingle();
            var m42 = _binaryReader.ReadSingle();

            var m13 = _binaryReader.ReadSingle();
            var m23 = _binaryReader.ReadSingle();
            var m33 = _binaryReader.ReadSingle();
            var m43 = _binaryReader.ReadSingle();

            value = new Matrix4x3(
                m11, m12, m13,
                m21, m22, m23,
                m31, m32, m33,
                m41, m42, m43);
        }

        public override void PersistMatrix4x3Transposed(ref Matrix4x3 value) => value = _binaryReader.ReadMatrix4x3Transposed();

        public override void PersistBitArray<TEnum>(ref BitArray<TEnum> result)
        {
            PersistVersion(1);

            result.SetAll(false);

            var stringToValueMap = Data.Ini.IniParser.GetEnumMap<TEnum>();

            var count = _binaryReader.ReadUInt32();

            for (var i = 0; i < count; i++)
            {
                var stringValue = "";
                PersistAsciiString(ref stringValue);

                var enumValue = (TEnum)stringToValueMap[stringValue];

                result.Set(enumValue, true);
            }
        }

        public override void PersistColorRgba(ref ColorRgba value) => value = _binaryReader.ReadColorRgba();

        public override void PersistColorRgbaInt(ref ColorRgba value) => value = _binaryReader.ReadColorRgbaInt();

        public override void PersistDateTime(ref DateTime value) => value = _binaryReader.ReadDateTime();

        public override void PersistRandomVariable(ref RandomVariable value) => value = _binaryReader.ReadRandomVariable();

        public override void PersistRandomAlphaKeyframe(ref RandomAlphaKeyframe value) => value = RandomAlphaKeyframe.ReadFromSaveFile(_binaryReader);

        public override void PersistRgbColorKeyframe(ref RgbColorKeyframe value) => value = RgbColorKeyframe.ReadFromSaveFile(_binaryReader);

        public override void PersistObjectNameAndIdSet(List<ObjectNameAndId> set)
        {
            PersistVersion(1);

            var numItems = (ushort)set.Count;
            PersistUInt16(ref numItems);

            for (var j = 0; j < numItems; j++)
            {
                var objectNameAndId = new ObjectNameAndId();

                PersistAsciiString(ref objectNameAndId.Name);
                PersistObjectID(ref objectNameAndId.ObjectId);

                set.Add(objectNameAndId);
            }
        }

        public override unsafe void ReadBytesIntoStream(Stream destination, int numBytes)
        {
            const int bufferSize = 1024;
            Span<byte> buffer = stackalloc byte[bufferSize];

            var numBytesRemaining = numBytes;
            while (numBytesRemaining > 0)
            {
                var currentSpan = buffer.Slice(0, Math.Min(numBytesRemaining, bufferSize));

                var numBytesRead = _binaryReader.BaseStream.Read(currentSpan);
                destination.Write(currentSpan);

                numBytesRemaining -= numBytesRead;
            }
        }

        public override uint BeginSegment(string segmentName)
        {
            if (SageGame >= SageGame.Bfme)
            {
                var blokHeader = _binaryReader.ReadFourCc(bigEndian: true);
                if (blokHeader != "BLOK")
                {
                    throw new InvalidStateException();
                }

                var segmentEnd = _binaryReader.ReadUInt32();

                var currentPosition = _binaryReader.BaseStream.Position;

                var segmentLength = (uint)(segmentEnd - currentPosition);

                Segments.Push(new Segment(currentPosition, segmentEnd, segmentName));

                return segmentLength;
            }
            else
            {
                var segmentLength = _binaryReader.ReadUInt32();

                var currentPosition = _binaryReader.BaseStream.Position;

                Segments.Push(new Segment(currentPosition, currentPosition + segmentLength, segmentName));

                return segmentLength;
            }
        }

        public override void EndSegment()
        {
            var segment = Segments.Pop();

            if (_binaryReader.BaseStream.Position != segment.End)
            {
                throw new InvalidStateException($"Stream position expected to be at 0x{segment.End:X8} but was at 0x{_binaryReader.BaseStream.Position:X8} while reading {segment.Name}");
            }
        }

        public override void SkipUnknownBytes(int numBytes)
        {
            for (var i = 0; i < numBytes; i++)
            {
                var unknown = _binaryReader.ReadByte();
                if (unknown != 0)
                {
                    throw new InvalidStateException($"Expected byte (index {i}) at position 0x{_binaryReader.BaseStream.Position - 1:X8} to be 0 but it was {unknown}");
                }
            }
        }
    }

    public sealed class InvalidStateException : Exception
    {
        public InvalidStateException()
            : base()
        {

        }

        public InvalidStateException(string message)
            : base(message)
        {

        }
    }

    public struct ObjectNameAndId
    {
        public string Name;
        public uint ObjectId;
    }
}
