using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using xayrga.bananapeel;


namespace DCM2XM
{
    class DCM2XM
    {
        public static int DCM_HEADER = 0x314D4344;
        public static int bpm = 125;


        public static uint freqFrom16(ushort v)
        {
            unchecked
            {
                byte exponent = (byte)(v >> 14);
                uint frequency = (uint)(v & 0x3fff);
                frequency = (ushort)((frequency << exponent) + 0x4000L * ((1 << exponent) - 1));
                return frequency;
            }
        }

        public static short[] PCM8216(byte[] adpdata)
        {
            short[] smplBuff = new short[adpdata.Length];
            for (int sam = 0; sam < adpdata.Length; sam++)
                smplBuff[sam] = (short)(adpdata[sam] * (adpdata[sam] < 0 ? 256 : 258));
            return smplBuff; // return
        }

        public static short[] PCM16toDPCM16(short[] pcmd)
        {
            short last = 0;
            short[] dpcm = new short[pcmd.Length];
            for (int i = 0; i < pcmd.Length; i++)
            {
                dpcm[i] = (short)(pcmd[i] - last);
                last = pcmd[i];
            }
            return dpcm;
        }

        public static sbyte[] PCM8toDPCM8(byte[] pcmd)
        {
            sbyte last = 0;        
            // initialize differential array 
            sbyte[] dpcm = new sbyte[pcmd.Length]; 
            for (int i = 0; i < pcmd.Length; i++)
            {
                // sign byte 
                sbyte signedPCM = (sbyte)(pcmd[i] > 127 ? pcmd[i] - 0xFF : pcmd[i]);
                // calculate differential 
                dpcm[i] = (sbyte)(signedPCM - last);
                // store pcm8 history 
                last = signedPCM;
            }
            return dpcm;
        }

        public unsafe static short[] PCM16ByteToShort(byte[] pcm)
        {

            // Initialize PCM16 array, half length (rounded upwards) 
            var pcmS = new short[(pcm.Length + 2 - 1) / 2];
            fixed (byte* pcmD = pcm) // Acquire pointer to byte[]
            {
                var pcmBy = (short*)pcmD; // cast pointer type to short
                for (int i = 0; i < pcmS.Length; i++) // fill buffer
                    pcmS[i] = pcmBy[i];
            }
            return pcmS;
        }
  
        static void Main(string[] args)
        {

            if (args.Length < 2)
            {
                usage();
                Environment.Exit(0);
            }
            var DCMFile = File.OpenRead(args[0]);
            var XMFile = File.OpenWrite(args[1]);
            var DCReader = new BinaryReader(DCMFile);
            var XMWriter = new BinaryWriter(XMFile);

            var DCMHeader = DCReader.ReadUInt32();
            if (DCMHeader != DCM_HEADER)
            {
                Console.WriteLine("Not a DCM file");
                return;
            }

            DCReader.BaseStream.Position = 0;
            var DCMData = DCMSong.read(DCReader);
            var DCMSongReader = new DCMSongReader(DCMData);

            for (int i = 0; i < DCMData.sampleCount; i++)
            {
                var currentSample = DCMData.samples[i];


                var wb = new PCM16WAV()
                {
                    format = 1,
                    sampleRate = 8000,
                    channels = 1,
                    blockAlign = 2,
                    bitsPerSample = 16,

                };

                FileStream wlx;

                if ((currentSample.flags & (int)DCMSampleFlags.PCM16) > 0)
                {
                    wlx = File.OpenWrite($"smp/{args[0]}_{i}_{currentSample.id}_pcm16.wav");
                    Console.WriteLine($"smp/{args[0]}_{i}_{currentSample.id} PCM16!!");
                    File.WriteAllBytes($"smp/{args[0]}_{i}_{currentSample.id}16.pcm", currentSample.pcmData);
                    wb.buffer = PCM16ByteToShort(currentSample.pcmData);
                }
                else
                {
                    Console.WriteLine($" {currentSample.id} PCM8");
                    wlx = File.OpenWrite($"smp/{args[0]}_{i}_{currentSample.id}_pcm8.wav");
                    File.WriteAllBytes($"smp/{args[0]}_{i}_{currentSample.id}8.pcm", currentSample.pcmData);
                    wb.buffer = PCM8216(currentSample.pcmData);
                }


                if ((currentSample.flags & (int)DCMSampleFlags.LOOP) > 0 || ((currentSample.flags & (int)DCMSampleFlags.BIDI) > 0))
                {
                    if (currentSample.loopstart > 0 | currentSample.loopend > 0)
                    {
                        wb.sampler.loops = new SampleLoop[1];
                        wb.sampler.loops[0] = new SampleLoop()
                        {
                            dwIdentifier = 0,
                            dwEnd = (int)currentSample.loopend,
                            dwFraction = 0,
                            dwPlayCount = 0,
                            dwStart = (int)currentSample.loopstart,
                            dwType = ((currentSample.flags & (int)DCMSampleFlags.BIDI) > 0) ? 1 : 0,
                        };
                    }
                }
                var wlxw = new BinaryWriter(wlx);
                wb.writeStreamLazy(wlxw);
                wlxw.Flush();
                wlxw.Close();

            }

            int rowCount = 0;
            while (DCMSongReader.nextRow())
                rowCount++;
            DCMSongReader.reader.BaseStream.Position = 0;

            Console.WriteLine($"Song has {rowCount} rows, {rowCount/256} patterns. and runs at {DCMSongReader.bpm}bpm");
            var dcmReader = new DCMSongReader(DCMData);


            var XMS = new XMSong();
            XMS.channelCount = DCMData.channelCount;
            

            var tW = new StringBuilder();
            var rows = 0;
            while (dcmReader.nextRow())
                rows++;

            XMS.patternCount = (short)((rows + 128 -1) / 128);
            XMS.patternOrderCount = XMS.patternCount;
            XMS.defaultBPM = 125;
            XMS.defaultTempo = 1;
            XMS.instrumentCount = 0;

            for (int i = 0; i < XMS.patternCount; i++)
                XMS.patternOrders[i] = (byte)i;

            XMS.patterns = new XMPattern[XMS.patternCount];

            for (int b = 0; b < XMS.patterns.Length; b++)
            {
                var newpat = new XMPattern();
                XMS.patterns[b] = newpat;
                newpat.rowCount = 128;
                newpat.rows = new XMRow[newpat.rowCount];
               
                for (int p = 0; p < newpat.rowCount; p++)
                {
                    var newRow = new XMRow();
                    newpat.rows[p] = newRow;
                    newRow.notes = new XMNote[XMS.channelCount];
                    for (int c = 0; c < XMS.channelCount; c++)
                        newRow.notes[c] = new XMNote();
                }
            }



            dcmReader.reader.BaseStream.Position = 0; // reset song lol

            byte[] channelInsruments = new byte[dcmReader.channels.Length];
            float[] lastFreq = new float[dcmReader.channels.Length];
            byte[] lastNote = new byte[dcmReader.channels.Length];
            short[] lastNoteFreqRemainder = new short[dcmReader.channels.Length];


            for (int i=0; i < channelInsruments.Length;i++)
            {
                channelInsruments[i] = 255;
                lastFreq[i] = 0; 
                lastNote[i] = 0;
                lastNoteFreqRemainder[i] = 0;
            }

            for (int i = 0; i < XMS.patterns.Length; i++)
            {
                var currentPat = XMS.patterns[i];
                for (int ri = 0; ri < currentPat.rows.Length; ri++)
                {
                    var xmRow = currentPat.rows[ri];
                    dcmReader.nextRow();
                    for (int channel = 0; channel < dcmReader.channels.Length; channel++)
                    {
                        var dcmChannel = dcmReader.channels[channel];
                        var xmNote = xmRow.notes[channel];


                        if ((dcmChannel.infoByte & (int)DCMSongReader.EVENT.FREQUENCY) > 0)
                        {
                            // If the sample kicks again after a changed frequency, our best bet is to restart the note
                            // Maybe instead we could use Rxx in sample?
                            var frq = freqFrom16(dcmChannel.frequency);
                            var remainder = (short)(dcmChannel.frequency % 0x100);
                            lastFreq[channel] = frq;
                            lastNoteFreqRemainder[channel] = remainder;

                            var minNote = 1 * 12;
                            var maxNote = 10 * 12 + 11;

                            // i am full of magic
                            //saturate_round(log(freq * (1.0 / 8363.0)) * (12.0 * 128.0 * (1.0 / M_LN2)));
                            var frequencyRatio = Math.Log((frq - 16000) / 16000f) * 12;
                            //Console.WriteLine($"{frequencyRatio}, { (byte)(Math.Round(frequencyRatio) + 0x41):X}");

                            lastNote[channel] = (byte)( Math.Round(frequencyRatio) + 0x41); 
                        }

                        if ((dcmChannel.infoByte & (int)DCMSongReader.EVENT.SAMPLE) > 0)
                        {

                            xmNote.Note = lastNote[channel];
                            if (channelInsruments[channel] != 255)
                            {
                                xmNote.Instrument = (byte)(channelInsruments[channel] + 1);
                                channelInsruments[channel] = 255;
                            }
                           
                        }

                        if ((dcmChannel.infoByte & (int)DCMSongReader.EVENT.NOTEOFF) > 0)
                        {
                            xmNote.Note = 97; // note off command in XM.
                        }
                   
                  
                        if ((dcmChannel.infoByte & (int)DCMSongReader.EVENT.INSTRUMENT) > 0)
                            channelInsruments[channel] = dcmChannel.instrument;

                        if ((dcmChannel.infoByte & (int)DCMSongReader.EVENT.SETBPM) > 0)
                        {
                            xmNote.Effect = 0xF;
                            xmNote.EffectParam = dcmReader.bpm;
                        }


                        if ((dcmChannel.infoByte & (int)DCMSongReader.EVENT.PAN) > 0)
                        {
                            xmNote.Effect = 0x08;
                            xmNote.EffectParam = dcmChannel.pan;
                        }

                        if ((dcmChannel.infoByte & (int)DCMSongReader.EVENT.VOLUME) > 0)
                            xmNote.Volume = (byte)((dcmChannel.volume / 4) + 0x10);


                        if ((dcmChannel.infoByte & (int)DCMSongReader.EVENT.OFFSET) > 0)
                        {
                            xmNote.Effect = 0x9;
                            xmNote.EffectParam = dcmChannel.offset;
                        }

                        if ((dcmChannel.infoByte & (int)DCMSongReader.EVENT.TRIGGR) > 0)
                            Console.WriteLine("TRIGGR"); 
                    }
                }

            }





            XMS.write(XMWriter);
          
            Console.ReadLine();

        }


        public static void usage()
        {
            Console.WriteLine("Usage: DCM2XM <dcmfile> <xmfile>");
        }
    }
}

