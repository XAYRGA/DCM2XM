using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;


namespace DCM2XM
{
    public static class XMUtil
    {
        public static void writePaddedString(BinaryWriter wrt, string str, int padd)
        {
            var name = Encoding.ASCII.GetBytes(str);
            wrt.BaseStream.Write(name, 0, name.Length);
            for (int i = 0; i < padd - str.Length; i++)
                wrt.Write((byte)0);
        }
    }
    public class XMSong
    {
        public string name = "XM Module";
        private string trackerName = "XAYRGA.XM";
        private string hSZ;

        public short patternOrderCount;
        public short restartPosition;
        public short channelCount;
        public short patternCount;
        public short instrumentCount;
        public short flags;  // Frequency Table;
        public short defaultTempo;
        public short defaultBPM;
        public byte[] patternOrders = new byte[0xFF];

        public XMPattern[] patterns;
        public XMInstrument[] instruments;

        public void write(BinaryWriter wrt)
        {
            var hed = Encoding.ASCII.GetBytes("Extended Module: ");
            wrt.BaseStream.Write(hed, 0, hed.Length);

            XMUtil.writePaddedString(wrt, name, 20);
            wrt.Write((byte)0x1A); // Revision.
            XMUtil.writePaddedString(wrt, trackerName, 20);
            wrt.Write((short)0x104);
            var anch = wrt.BaseStream.Position;
            wrt.Write(0); // Header size, we'll come back to this later. 
            wrt.Write(patternOrderCount);
            wrt.Write(restartPosition);
            wrt.Write(channelCount);
            wrt.Write(patternCount);
            wrt.Write(instrumentCount);
            wrt.Write(flags);
            wrt.Write(defaultTempo);
            wrt.Write(defaultBPM);
            wrt.BaseStream.Write(patternOrders, 0, 0xFF);
            wrt.Write((byte)0);
            var end = wrt.BaseStream.Position;
            wrt.BaseStream.Position = anch;
            wrt.Write((int)(end - anch));
            wrt.BaseStream.Position = end;
            for (int i = 0; i < patternCount; i++)
                patterns[i].write(wrt);
            
        }

    }

    public class XMPattern
    {
        public int length;
        public byte packType = 1;
        public short rowCount;
        public short dataSize;
        public XMRow[] rows;

        public void write(BinaryWriter wrt)
        {
            var begin = wrt.BaseStream.Position;
            wrt.Write(9);
            wrt.Write(packType);
            wrt.Write(rowCount);
            wrt.Write((short)0);
            var beginPat = wrt.BaseStream.Position;
            for (int i = 0; i < rowCount; i++)
                rows[i].write(wrt);

            var end = wrt.BaseStream.Position;
            wrt.BaseStream.Position = begin + 7;
            wrt.Write((short)(end - beginPat));
            wrt.BaseStream.Position = end;
        }

    }

    public class XMRow {

        public XMNote[] notes;

        public void write(BinaryWriter wrt)
        {
            for (int i = 0; i < notes.Length; i++)
                notes[i].write(wrt);
        }
    }

    public class XMInstrument
    {
        public int sampleHeaderSize;
        public string instrumentName = "";
        public byte type = 0; 
        public byte[] sampleMap = new byte[96];
        public byte[] volEnvelope = new byte[48];
        public byte[] panEnvelope = new byte[48];
        public byte volEnvLength;
        public byte panEnvLength;
        public byte volSustainPoint;
        public byte volLoopStart;
        public byte volLoopEnd;
        public byte panSustainPoint;
        public byte panLoopStart;
        public byte panLoopEnd;
        public byte volType;
        public byte panType;
        public byte vibratoType;
        public byte vibratoSweep;
        public byte vibradoDepth;
        public byte vibratoRate;
        public short volumeFadeout;
        public short reserved;
        public XMSample[] samples;

        public void mapAllSamples(byte smp)
        {
            for (int i = 0; i < sampleMap.Length; i++)
                sampleMap[i] = smp;
        }

        public void write(BinaryWriter wrt)
        {
            var hSizeAnchor = wrt.BaseStream.Position;
            wrt.Write(0); // Template size;        
            XMUtil.writePaddedString(wrt, instrumentName, 22);
            wrt.Write(type);
            wrt.Write(samples.Length);
            if (samples.Length == 0)
                goto finishInstrumentHeader;
            wrt.BaseStream.Write(sampleMap, 0, sampleMap.Length);
            wrt.BaseStream.Write(panEnvelope, 0, panEnvelope.Length);
            wrt.BaseStream.Write(volEnvelope, 0, volEnvelope.Length);
            wrt.Write(volEnvLength);
            wrt.Write(panEnvLength);
            wrt.Write(volSustainPoint);
            wrt.Write(volLoopStart);
            wrt.Write(volLoopEnd);
            wrt.Write(panSustainPoint);
            wrt.Write(panLoopStart);
            wrt.Write(panLoopEnd);
            wrt.Write(volType);
            wrt.Write(panType);
            wrt.Write(vibratoType);
            wrt.Write(vibratoSweep);
            wrt.Write(vibradoDepth);
            wrt.Write(vibratoRate);
            wrt.Write(volumeFadeout);
            wrt.Write(reserved);

            finishInstrumentHeader:

            var hFinAnchor = wrt.BaseStream.Position;
            wrt.BaseStream.Position = hSizeAnchor;
            wrt.Write(hFinAnchor - hSizeAnchor);
            wrt.BaseStream.Position = hFinAnchor;
        }

    }


    public class XMSample {

        public int length;
        public int loopStart;
        public int loopEnd;
        public byte volume = 64;
        public byte type;
        public byte pan;
        public sbyte note;
        public byte reserved;
        public string sampleName;
        public byte[] dpcm;

        public void writeDPCM8(byte[] pcm8)
        {
            var last = 0; 
          
        }

        public unsafe void writeDPCM16(short[] pcm16)
        {
            short[] dpcm16 = new short[pcm16.Length];
            var last = 0;
            for (int i = 0; i < pcm16.Length; i++)
            {
                dpcm16[i] = (short)(pcm16[i] - last);
                last = pcm16[i];
            }
            dpcm = new byte[dpcm16.Length * 2];
            fixed (short* pcmP = dpcm16)
            {
                byte* bP = (byte*)pcmP;
                for (int i = 0; i < dpcm16.Length * 2; i++)
                    dpcm[i] = bP[i];
            }
        }

        public unsafe void writeDPCM16Slow(short[] pcm16)
        {
            short[] dpcm16 = new short[pcm16.Length];
            var last = 0;
            for (int i = 0; i < pcm16.Length; i++)
            {
                dpcm16[i] = (short)(pcm16[i] - last);
                last = pcm16[i];
            }
            dpcm = new byte[dpcm16.Length * 2];

            var pcmSampleIndex = 0;
            for (int i = 0; i < dpcm.Length; i += 2)
            {
                dpcm[i] = (byte)(dpcm16[pcmSampleIndex] & 0xFF);
                dpcm[i + 1] = (byte)(dpcm16[pcmSampleIndex] >> 0x08);
                pcmSampleIndex++;
            }
        }


        public void write(BinaryWriter wrt)
        {

        }
    }

    public class XMNote
    {
        private byte mask = 0;
        private byte note;
        private byte inst = 0;
        private byte vol = 0;
        private byte eff = 0;
        private byte effPar = 0;

        public byte Note
        {
            get
            {
                return note; 
            }
            set
            {
                mask |= 0x01;
                note = value;
            }
        }

        public byte Instrument
        {
            get
            {
                return inst;
            }
            set
            {
                mask |= 0x02;
                inst = value;
            }
        }
        public byte Volume
        {
            get
            {
                return vol;
            }
            set
            {
                mask |= 0x04;
                vol = value;
            }
        }

        public byte Effect
        {
            get
            {
                return eff;
            }
            set
            {
                mask |= 0x08;
                eff = value;
            }
        }

        public byte EffectParam
        {
            get
            {
                return effPar;
            }
            set
            {
                mask |= 0x10;
                effPar = value;
            }
        }

        public void write(BinaryWriter wrt)
        {
            wrt.Write((byte)(mask | 0x80));
            if ((mask & 0x01) > 0)
                wrt.Write(note);
            if ((mask & 2) > 0)
                wrt.Write(inst);
            if ((mask & 4) > 0)
                wrt.Write(vol);
            if ((mask & 8) > 0)
                wrt.Write(eff);
            if ((mask & 16) > 0)
                wrt.Write(effPar);
        }
    }
}

