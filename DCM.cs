using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace DCM2XM
{
    public enum DCMSampleFlags
    {
        PCM16 = 0x1,
        SIGNED = 0x2,
        LOOP = 0x4,
        BIDI = 0x8,
    }

    public enum DCMPatternEvents
    {
        DELAY = 0x80,
        FREQUENCY = 0x40,
        VOLUME = 0x20,
        PAN = 0x10,
        INSTRUMENT = 0x08,
        OFFSET = 0x04,
        SAMPLE = 0x02,
        CHAIN = 0x01,
    }

    public enum DCMPatternEventsExtended
    {
        SETBPM = 0x40,
        NOTEOFF = 0x80,
        TRIGGR = 0x20,
        CHAIN = 0x01
    }


    public class DCMSample
    {
        public uint length;
        public uint loopstart;
        public uint loopend;
        public ushort flags;
        public ushort id;

        public byte[] pcmData;

        public static DCMSample read(BinaryReader reader)
        {
            var DCM = new DCMSample()
            {
                length = reader.ReadUInt32(),
                loopstart = reader.ReadUInt32(),
                loopend = reader.ReadUInt32(),
                flags = reader.ReadUInt16(),
                id = reader.ReadUInt16()
            };
            return DCM;
        }

        public void readBody(BinaryReader reader)
        {
            pcmData = reader.ReadBytes((int)length * ((flags & (int)DCMSampleFlags.PCM16) > 0 ? 2 : 1));
        }
    }

    public class DCMSong
    {
        public uint header;
        public byte channelCount;
        public byte sampleCount;
        public uint patternSize;
        public uint parrernRepeat;
        public DCMSample[] samples;
        public byte[] pattern;

        public static DCMSong read(BinaryReader reader)
        {
            var DCM = new DCMSong()
            {
                header = reader.ReadUInt32(),
                channelCount = reader.ReadByte(),
                sampleCount = reader.ReadByte(),
                patternSize = reader.ReadUInt32(),
                parrernRepeat = reader.ReadUInt32(),
            };
            DCM.samples = new DCMSample[DCM.sampleCount];
            for (int i = 0; i < DCM.sampleCount; i++)
                DCM.samples[i] = DCMSample.read(reader);
            DCM.pattern = reader.ReadBytes((int)DCM.patternSize);
            for (int i = 0; i < DCM.sampleCount; i++)
                DCM.samples[i].readBody(reader);
            return DCM;
        }
    }

    public class DCMSongReader
    {
        public class DCMChannel
        {
            public byte id;
            public byte instrument;
            public byte pan;
            public byte volume;
            public ushort frequency;
            public ushort infoByte;
            public byte trigger;
            public byte offset;
        }

        public enum EVENT
        {
            SETBPM = 0x40 << 8,
            NOTEOFF = 0x80 << 8,
            TRIGGR = 0x20 << 8,
            CHAINEXTENDED = 0x01 << 8,
            DELAY = 0x80,
            FREQUENCY = 0x40,
            VOLUME = 0x20,
            PAN = 0x10,
            INSTRUMENT = 0x08,
            OFFSET = 0x04,
            SAMPLE = 0x02,
            CHAIN = 0x01,
        }

        public BinaryReader reader;
        public DCMSong song;
        public int delay;
        public DCMChannel[] channels;
        public byte bpm;
    

        public DCMSongReader(DCMSong ng)
        {
            bpm = 125;
            song = ng;
            reader = new BinaryReader(new MemoryStream(song.pattern));
            channels = new DCMChannel[song.channelCount];
            for (byte i = 0; i < song.channelCount; i++)
                channels[i] = new DCMChannel() { id = i };
        }

        public void reset()
        {
            reader.BaseStream.Position = 0;
            delay = 0;
            for (byte i = 0; i < song.channelCount; i++)
            {
                channels[i].infoByte = 0;
            }
            bpm = 125;
        }

        private bool checkEvent(byte op, DCMPatternEvents evt, ref ushort data)
        {
            if ((op & (int)evt) > 0)
            {
                data |= (ushort)((int)(evt));
                return true;
            }
            return false;
        }


        private bool checkEvent(byte op, DCMPatternEventsExtended evt, ref ushort data)
        {
            if ((op & (int)evt) > 0)
            {
                data |= (ushort)((int)evt << 8);
                return true;
            }
            return false;
        }



        public bool nextRow()
        {
            for (int channel = 0; channel < song.channelCount; channel++)
            {
                var currentChannel = channels[channel];
                currentChannel.infoByte = 0; // reset status
                delay--; // decrement delay
                if (delay >= 0)
                    continue; // We have to emulate resetting status byte until nops run out, otherwise delay gets shifted

                if (reader.BaseStream.Position >= song.patternSize)
                {
                    reader.BaseStream.Position = song.parrernRepeat;
                    return false;
                }


                var opcode = reader.ReadByte();
                
                // Delay
                if (checkEvent(opcode, DCMPatternEvents.DELAY, ref currentChannel.infoByte))
                {
                    delay = opcode & 0x7F; // Extract delay from lower 7 bits; 
                    continue; // Terminate
                }

                // Note frequency select
                if (checkEvent(opcode, DCMPatternEvents.FREQUENCY, ref currentChannel.infoByte))
                    currentChannel.frequency = reader.ReadUInt16();

                // Volume set
                if (checkEvent(opcode, DCMPatternEvents.VOLUME, ref currentChannel.infoByte))
                    currentChannel.volume = reader.ReadByte();

                // Pan set
                if (checkEvent(opcode, DCMPatternEvents.PAN, ref currentChannel.infoByte))
                    currentChannel.pan = reader.ReadByte();

                //Instrument Select
                if (checkEvent(opcode, DCMPatternEvents.INSTRUMENT, ref currentChannel.infoByte))
                    currentChannel.instrument = reader.ReadByte();

                // Sample playback offset
                if (checkEvent(opcode, DCMPatternEvents.OFFSET, ref currentChannel.infoByte))
                    currentChannel.offset = reader.ReadByte();
                // "SAMPLE" or "Note On" , Freuqency must be set before note
                checkEvent(opcode, DCMPatternEvents.SAMPLE, ref currentChannel.infoByte);
                    
                // Extended commands
                if (checkEvent(opcode, DCMPatternEvents.CHAIN, ref currentChannel.infoByte))
                {
                    var extendedOpcode = reader.ReadByte();
                    // what? 
                    // Extended call into subcommand chain just for a note off?
                    // >:(
                    checkEvent(extendedOpcode, DCMPatternEventsExtended.NOTEOFF, ref currentChannel.infoByte);

                    if (checkEvent(extendedOpcode, DCMPatternEventsExtended.SETBPM, ref currentChannel.infoByte))
                        bpm = reader.ReadByte();
                    if (checkEvent(extendedOpcode, DCMPatternEventsExtended.TRIGGR, ref currentChannel.infoByte))
                        currentChannel.trigger = reader.ReadByte(); // Trigger ID
                }
            }
            return true;
        }
    }
}
