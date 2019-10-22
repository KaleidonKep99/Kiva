﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.WPF;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using SharpDX.Direct3D;
using System.Collections.Concurrent;
using IO = System.IO;
using System.Reflection;

namespace Kiva_MIDI
{
    class MIDIRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        struct NotesGlobalConstants
        {
            public float NoteLeft;
            public float NoteRight;
            public float NoteBorder;
            public float ScreenAspect;
            public float KeyboardHeight;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        struct KeyboardGlobalConstants
        {
            public float Height;
            public float Left;
            public float Right;
            public float Aspect;
            public uint BarColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RenderNote
        {
            public float start;
            public float end;
            public NoteCol color;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RenderKey
        {
            public uint colorl;
            public uint colorr;
            public float left;
            public float right;
            public float distance;
            public uint meta;

            public void MarkPressed(bool ispressed)
            {
                meta = (uint)(meta & 0b10111111);
                if (ispressed)
                    meta = (uint)(meta | 0b01);
            }

            public void MarkBlack(bool black)
            {
                meta = (uint)(meta & 0b01111111);
                if (black)
                    meta = (uint)(meta | 0b1);
            }
        }

        private MIDIFile file;
        public MIDIFile File
        {
            get => file;
            set
            {
                lock (fileLock)
                {
                    file = value;
                }
            }
        }

        object fileLock = new object();
        public PlayingState Time { get; set; } = new PlayingState();

        public long LastRenderedNoteCount { get; private set; } = 0;

        ShaderManager notesShader;
        ShaderManager SmallWhiteKeyShader;
        ShaderManager SmallBlackKeyShader;
        ShaderManager BigWhiteKeyShader;
        ShaderManager BigBlackKeyShader;
        ShaderManager BigBarShader;
        InputLayout noteLayout;
        InputLayout keyLayout;
        Buffer globalNoteConstants;
        Buffer keyBuffer;

        NotesGlobalConstants noteConstants;

        int noteBufferLength = 1 << 12;
        Buffer noteBuffer;

        VelocityEase dynamicState = new VelocityEase(0) { Duration = 0.7, Slope = 3, Supress = 2 };
        bool dynamicState88 = false;

        bool[] blackKeys = new bool[257];
        int[] keynum = new int[257];
        int[] blackKeysID;
        int[] whiteKeysID;

        RenderKey[] renderKeys = new RenderKey[257];
        VelocityEase[] keyEases = new VelocityEase[256];

        Settings settings;

        public MIDIRenderer(Device device, Settings settings)
        {
            this.settings = settings;
            string noteShaderData;
            if (IO.File.Exists("Notes.fx"))
            {
                noteShaderData = IO.File.ReadAllText("Notes.fx");
            }
            else
            {
                var assembly = Assembly.GetExecutingAssembly();
                var names = assembly.GetManifestResourceNames();
                using (var stream = assembly.GetManifestResourceStream("Kiva_MIDI.Notes.fx"))
                using (var reader = new IO.StreamReader(stream))
                    noteShaderData = reader.ReadToEnd();
            }
            notesShader = new ShaderManager(
                device,
                ShaderBytecode.Compile(noteShaderData, "VS_Note", "vs_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "PS", "ps_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "GS_Note", "gs_4_0", ShaderFlags.None, EffectFlags.None)
            );

            if (IO.File.Exists("KeyboardSmall.fx"))
            {
                noteShaderData = IO.File.ReadAllText("KeyboardSmall.fx");
            }
            else
            {
                var assembly = Assembly.GetExecutingAssembly();
                var names = assembly.GetManifestResourceNames();
                using (var stream = assembly.GetManifestResourceStream("Kiva_MIDI.KeyboardSmall.fx"))
                using (var reader = new IO.StreamReader(stream))
                    noteShaderData = reader.ReadToEnd();
            }
            SmallWhiteKeyShader = new ShaderManager(
                device,
                ShaderBytecode.Compile(noteShaderData, "VS", "vs_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "PS", "ps_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "GS_White", "gs_4_0", ShaderFlags.None, EffectFlags.None)
            );
            SmallBlackKeyShader = new ShaderManager(
                device,
                ShaderBytecode.Compile(noteShaderData, "VS", "vs_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "PS", "ps_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "GS_Black", "gs_4_0", ShaderFlags.None, EffectFlags.None)
            );

            if (IO.File.Exists("KeyboardBig.fx"))
            {
                noteShaderData = IO.File.ReadAllText("KeyboardBig.fx");
            }
            else
            {
                var assembly = Assembly.GetExecutingAssembly();
                var names = assembly.GetManifestResourceNames();
                using (var stream = assembly.GetManifestResourceStream("Kiva_MIDI.KeyboardBig.fx"))
                using (var reader = new IO.StreamReader(stream))
                    noteShaderData = reader.ReadToEnd();
            }
            BigWhiteKeyShader = new ShaderManager(
                device,
                ShaderBytecode.Compile(noteShaderData, "VS", "vs_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "PS", "ps_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "GS_White", "gs_4_0", ShaderFlags.None, EffectFlags.None)
            );
            BigBlackKeyShader = new ShaderManager(
                device,
                ShaderBytecode.Compile(noteShaderData, "VS", "vs_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "PS", "ps_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "GS_Black", "gs_4_0", ShaderFlags.None, EffectFlags.None)
            );
            BigBarShader = new ShaderManager(
                device,
                ShaderBytecode.Compile(noteShaderData, "VS", "vs_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "PS", "ps_4_0", ShaderFlags.None, EffectFlags.None),
                ShaderBytecode.Compile(noteShaderData, "GS_Bar", "gs_4_0", ShaderFlags.None, EffectFlags.None)
            );

            noteLayout = new InputLayout(device, ShaderSignature.GetInputSignature(notesShader.vertexShaderByteCode), new[] {
                new InputElement("START",0,Format.R32_Float,0,0),
                new InputElement("END",0,Format.R32_Float,4,0),
                new InputElement("COLORL",0,Format.R32_UInt,8,0),
                new InputElement("COLORR",0,Format.R32_UInt,12,0),
            });

            keyLayout = new InputLayout(device, ShaderSignature.GetInputSignature(SmallWhiteKeyShader.vertexShaderByteCode), new[] {
                new InputElement("COLORL",0,Format.R32_UInt,0,0),
                new InputElement("COLORR",0,Format.R32_UInt,4,0),
                new InputElement("LEFT",0,Format.R32_Float,8,0),
                new InputElement("RIGHT",0,Format.R32_Float,12,0),
                new InputElement("DISTANCE",0,Format.R32_Float,16,0),
                new InputElement("META",0,Format.R32_UInt,20,0),
            });

            noteConstants = new NotesGlobalConstants()
            {
                NoteBorder = 0.002f,
                NoteLeft = -0.2f,
                NoteRight = 0.0f,
                ScreenAspect = 1f
            };

            noteBuffer = new Buffer(device, new BufferDescription()
            {
                BindFlags = BindFlags.VertexBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                SizeInBytes = 40 * noteBufferLength,
                Usage = ResourceUsage.Dynamic,
                StructureByteStride = 0
            });

            keyBuffer = new Buffer(device, new BufferDescription()
            {
                BindFlags = BindFlags.VertexBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                SizeInBytes = 24 * renderKeys.Length,
                Usage = ResourceUsage.Dynamic,
                StructureByteStride = 0
            });

            globalNoteConstants = new Buffer(device, new BufferDescription()
            {
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                SizeInBytes = 32,
                Usage = ResourceUsage.Dynamic,
                StructureByteStride = 0
            });

            for (int i = 0; i < blackKeys.Length; i++) blackKeys[i] = isBlackNote(i);
            int b = 0;
            int w = 0;
            List<int> black = new List<int>();
            List<int> white = new List<int>();
            for (int i = 0; i < keynum.Length; i++)
            {
                if (blackKeys[i])
                {
                    keynum[i] = b++;
                    if (i < 256)
                        black.Add(i);
                }
                else
                {
                    keynum[i] = w++;
                    if (i < 256)
                        white.Add(i);
                }
            }

            blackKeysID = black.ToArray();
            whiteKeysID = white.ToArray();

            int firstNote = 0;
            int lastNote = 256;

            double wdth;

            double blackKeyScale = 0.65;
            double offset2set = 0.3;
            double offset3set = 0.5;

            double knmfn = keynum[firstNote];
            double knmln = keynum[lastNote - 1];
            if (blackKeys[firstNote]) knmfn = keynum[firstNote - 1] + 0.5;
            if (blackKeys[lastNote - 1]) knmln = keynum[lastNote] - 0.5;
            for (int i = 0; i < 257; i++)
            {
                if (!blackKeys[i])
                {
                    x1array[i] = (float)(keynum[i] - knmfn) / (knmln - knmfn + 1);
                    wdtharray[i] = 1.0f / (knmln - knmfn + 1);
                }
                else
                {
                    int _i = i + 1;
                    wdth = blackKeyScale / (knmln - knmfn + 1);
                    int bknum = keynum[i] % 5;
                    double offset = wdth / 2;
                    if (bknum == 0) offset += offset * offset2set;
                    if (bknum == 2) offset += offset * offset3set;
                    if (bknum == 1) offset -= offset * offset2set;
                    if (bknum == 4) offset -= offset * offset3set;

                    x1array[i] = (float)(keynum[_i] - knmfn) / (knmln - knmfn + 1) - offset;
                    wdtharray[i] = wdth;
                    renderKeys[i].MarkBlack(true);
                }
                renderKeys[i].left = (float)x1array[i];
                renderKeys[i].right = (float)(x1array[i] + wdtharray[i]);
            }

            for (int i = 0; i < keyEases.Length; i++)
            {
                keyEases[i] = new VelocityEase(0) { Duration = 0.05, Slope = 2, Supress = 3 };
            }

            var renderTargetDesc = new RenderTargetBlendDescription();
            renderTargetDesc.IsBlendEnabled = true;
            renderTargetDesc.SourceBlend = BlendOption.SourceAlpha;
            renderTargetDesc.DestinationBlend = BlendOption.InverseSourceAlpha;
            renderTargetDesc.BlendOperation = BlendOperation.Add;
            renderTargetDesc.SourceAlphaBlend = BlendOption.One;
            renderTargetDesc.DestinationAlphaBlend = BlendOption.One;
            renderTargetDesc.AlphaBlendOperation = BlendOperation.Add;
            renderTargetDesc.RenderTargetWriteMask = ColorWriteMaskFlags.All;

            BlendStateDescription desc = new BlendStateDescription();
            desc.AlphaToCoverageEnable = false;
            desc.IndependentBlendEnable = false;
            desc.RenderTarget[0] = renderTargetDesc;

            var blendStateEnabled = new BlendState(device, desc);

            device.ImmediateContext.OutputMerger.SetBlendState(blendStateEnabled);

            RasterizerStateDescription renderStateDesc = new RasterizerStateDescription
            {
                CullMode = CullMode.None,
                DepthBias = 0,
                DepthBiasClamp = 0,
                FillMode = FillMode.Solid,
                IsAntialiasedLineEnabled = false,
                IsDepthClipEnabled = false,
                IsFrontCounterClockwise = false,
                IsMultisampleEnabled = true,
                IsScissorEnabled = false,
                SlopeScaledDepthBias = 0
            };
            var rasterStateSolid = new RasterizerState(device, renderStateDesc);
            device.ImmediateContext.Rasterizer.State = rasterStateSolid;
        }

        void SetNoteShaderConstants(DeviceContext context, NotesGlobalConstants constants)
        {
            DataStream data;
            context.MapSubresource(globalNoteConstants, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out data);
            data.Write(constants);
            context.UnmapSubresource(globalNoteConstants, 0);
            context.VertexShader.SetConstantBuffer(0, globalNoteConstants);
            context.GeometryShader.SetConstantBuffer(0, globalNoteConstants);
        }

        void SetKeyboardShaderConstants(DeviceContext context, KeyboardGlobalConstants constants)
        {
            DataStream data;
            context.MapSubresource(globalNoteConstants, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out data);
            data.Write(constants);
            context.UnmapSubresource(globalNoteConstants, 0);
            context.VertexShader.SetConstantBuffer(0, globalNoteConstants);
            context.GeometryShader.SetConstantBuffer(0, globalNoteConstants);
        }

        double[] x1array = new double[257];
        double[] wdtharray = new double[257];
        bool[] pressedKeys = new bool[256];

        public void Render(Device device, RenderTargetView target, DrawEventArgs args)
        {
            var context = device.ImmediateContext;
            context.InputAssembler.InputLayout = noteLayout;

            double time = Time.GetTime();
            double timeScale = settings.Volatile.Size;
            double renderCutoff = time + timeScale;
            int firstNote = 0;
            int lastNote = 128;
            if (settings.General.KeyRange == KeyRangeTypes.KeyDynamic ||
                settings.General.KeyRange == KeyRangeTypes.Key128)
            {
                firstNote = 0;
                lastNote = 128;
            }
            else if (settings.General.KeyRange == KeyRangeTypes.Key256)
            {
                firstNote = 0;
                lastNote = 256;
            }
            else if (settings.General.KeyRange == KeyRangeTypes.Key88)
            {
                firstNote = 21;
                lastNote = 108;
            }
            else if (settings.General.KeyRange == KeyRangeTypes.Custom)
            {
                firstNote = settings.General.CustomFirstKey;
                lastNote = settings.General.CustomLastKey + 1;
            }
            else if (settings.General.KeyRange == KeyRangeTypes.KeyMIDI)
            {
                if (File != null)
                {
                    firstNote = File.FirstKey;
                    lastNote = File.LastKey + 1;
                }
            }
            int kbfirstNote = firstNote;
            int kblastNote = lastNote;
            if (blackKeys[firstNote]) kbfirstNote--;
            if (blackKeys[lastNote - 1]) kblastNote++;


            notesShader.SetShaders(context);
            noteConstants.ScreenAspect = (float)(args.RenderSize.Height / args.RenderSize.Width);
            noteConstants.NoteBorder = 0.0015f;
            SetNoteShaderConstants(context, noteConstants);

            //context.ClearRenderTargetView(target, new Color4(0.4f, 0.4f, 0.4f, 1f));
            context.ClearRenderTargetView(target, new Color4(0.0f, 0.0f, 0.0f, 0f));

            double ds = dynamicState.GetValue(0, 1);

            double fullLeft = x1array[firstNote];
            double fullRight = x1array[lastNote - 1] + wdtharray[lastNote - 1];
            if (settings.General.KeyRange == KeyRangeTypes.KeyDynamic)
            {
                double kleft = x1array[21];
                double kright = x1array[108] + wdtharray[108];
                fullLeft = fullLeft * (1 - ds) + kleft * ds;
                fullRight = fullRight * (1 - ds) + kright * ds;
            }
            double fullWidth = fullRight - fullLeft;

            float kbHeight = (float)(args.RenderSize.Width / args.RenderSize.Height / fullWidth);
            if (settings.General.KeyboardStyle == KeyboardStyle.Small) kbHeight *= 0.017f;
            if (settings.General.KeyboardStyle == KeyboardStyle.Big) kbHeight *= 0.04f;
            if (settings.General.KeyboardStyle == KeyboardStyle.None) kbHeight = 0;
            noteConstants.KeyboardHeight = kbHeight;

            lock (fileLock)
            {
                if (File != null)
                {
                    if (File is MIDIMemoryFile)
                    {
                        var file = File as MIDIMemoryFile;
                        file.SetColorEvents(time);

                        var colors = file.MidiNoteColors;
                        var lastTime = file.lastRenderTime;

                        long notesRendered = 0;
                        object addLock = new object();

                        int firstRenderKey = 256;
                        int lastRenderKey = -1;

                        int[] ids;

                        for (int black = 0; black < 2; black++)
                        {
                            if (black == 1) ids = blackKeysID;
                            else ids = whiteKeysID;
                            Parallel.ForEach(ids, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount }, k =>
                            {
                                long _notesRendered = 0;
                                float left = (float)((x1array[k] - fullLeft) / fullWidth);
                                float right = (float)((x1array[k] + wdtharray[k] - fullLeft) / fullWidth);
                                bool pressed = false;
                                NoteCol col = new NoteCol();
                                unsafe
                                {
                                    RenderNote* rn = stackalloc RenderNote[noteBufferLength];
                                    int nid = 0;
                                    int noff = file.FirstRenderNote[k];
                                    Note[] notes = file.Notes[k];
                                    if (notes.Length == 0) goto skipLoop;
                                    if (lastTime > time)
                                    {
                                        for (noff = 0; noff < notes.Length; noff++)
                                        {
                                            if (notes[noff].end > time)
                                            {
                                                break;
                                            }
                                        }
                                        file.FirstRenderNote[k] = noff;
                                    }
                                    else if (lastTime < time)
                                    {
                                        for (; noff < notes.Length; noff++)
                                        {
                                            if (notes[noff].end > time)
                                            {
                                                break;
                                            }
                                        }
                                        file.FirstRenderNote[k] = noff;
                                    }
                                    while (noff != notes.Length && notes[noff].start < renderCutoff)
                                    {
                                        var n = notes[noff++];
                                        if (n.end < time) continue;
                                        if (n.start < time)
                                        {
                                            pressed = true;
                                            NoteCol kcol = file.MidiNoteColors[n.colorPointer];
                                            col.rgba = NoteCol.Blend(col.rgba, kcol.rgba);
                                            col.rgba2 = NoteCol.Blend(col.rgba2, kcol.rgba2);
                                        }
                                        _notesRendered++;
                                        rn[nid++] = new RenderNote()
                                        {
                                            start = (float)((n.start - time) / timeScale),
                                            end = (float)((n.end - time) / timeScale),
                                            color = colors[n.colorPointer]
                                        };
                                        if (nid == noteBufferLength)
                                        {
                                            FlushNoteBuffer(context, left, right, (IntPtr)rn, nid);
                                            nid = 0;
                                        }
                                    }
                                    FlushNoteBuffer(context, left, right, (IntPtr)rn, nid);
                                    skipLoop:
                                    renderKeys[k].colorl = col.rgba;
                                    renderKeys[k].colorr = col.rgba2;
                                    if (pressed && keyEases[k].End == 0) keyEases[k].SetEnd(1);
                                    else if (!pressed && keyEases[k].End == 1) keyEases[k].SetEnd(0);
                                    renderKeys[k].distance = (float)keyEases[k].GetValue(0, 1);
                                    lock (addLock)
                                    {
                                        notesRendered += _notesRendered;
                                        if (_notesRendered > 0)
                                        {
                                            if (firstRenderKey > k) firstRenderKey = k;
                                            if (lastRenderKey < k) lastRenderKey = k;
                                        }
                                    }
                                }
                            });
                        }
                        if (firstRenderKey <= 19 || lastRenderKey >= 110)
                        {
                            if (dynamicState88)
                            {
                                dynamicState.SetEnd(0);
                                dynamicState88 = false;
                            }
                        }
                        else
                        {
                            if (!dynamicState88)
                            {
                                dynamicState.SetEnd(1);
                                dynamicState88 = true;
                            }
                        }

                        LastRenderedNoteCount = notesRendered;
                        file.lastRenderTime = time;
                    }
                }
                else
                {
                    LastRenderedNoteCount = 0;
                    for (int i = 0; i < renderKeys.Length; i++)
                    {
                        renderKeys[i].colorl = 0;
                        renderKeys[i].colorr = 0;
                        renderKeys[i].distance = 0;
                    }
                }
            }

            if (settings.General.KeyboardStyle != KeyboardStyle.None)
            {
                DataStream data;
                var col = settings.General.BarColor;
                SetKeyboardShaderConstants(context, new KeyboardGlobalConstants()
                {
                    Height = kbHeight,
                    Left = (float)fullLeft,
                    Right = (float)fullRight,
                    Aspect = noteConstants.ScreenAspect,
                    BarColor = NoteCol.Compress(col.R, col.G, col.B, col.A)
                });
                context.InputAssembler.InputLayout = keyLayout;
                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.PointList;
                context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(keyBuffer, 24, 0));
                context.MapSubresource(keyBuffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out data);
                data.Position = 0;
                data.WriteRange(renderKeys, 0, 257);
                context.UnmapSubresource(keyBuffer, 0);
                data.Close();
                if (settings.General.KeyboardStyle == KeyboardStyle.Big)
                {
                    BigWhiteKeyShader.SetShaders(context);
                    context.Draw(257, 0);
                    BigBarShader.SetShaders(context);
                    context.Draw(1, 0);
                    BigBlackKeyShader.SetShaders(context);
                    context.Draw(257, 0);
                }
                else
                {
                    SmallWhiteKeyShader.SetShaders(context);
                    context.Draw(257, 0);
                    SmallBlackKeyShader.SetShaders(context);
                    context.Draw(257, 0);
                }
            }
        }

        unsafe void FlushNoteBuffer(DeviceContext context, float left, float right, IntPtr notes, int count)
        {
            if (count == 0) return;
            lock (context)
            {
                DataStream data;
                context.MapSubresource(noteBuffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None, out data);
                data.Position = 0;
                data.WriteRange(notes, count * sizeof(RenderNote));
                context.UnmapSubresource(noteBuffer, 0);
                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.PointList;
                context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(noteBuffer, 16, 0));
                noteConstants.NoteLeft = left;
                noteConstants.NoteRight = right;
                SetNoteShaderConstants(context, noteConstants);
                context.Draw(count, 0);
            }
        }

        private DeviceContext GetInternalContext(Device device)
        {
            var props = device.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var prop = device.GetType().GetField("Context", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            DeviceContext context = prop.GetValue(device) as DeviceContext;
            return context;
        }

        public void Dispose()
        {
            Disposer.SafeDispose(ref noteLayout);
            Disposer.SafeDispose(ref keyLayout);
            Disposer.SafeDispose(ref notesShader);
            Disposer.SafeDispose(ref SmallWhiteKeyShader);
            Disposer.SafeDispose(ref SmallBlackKeyShader);
            Disposer.SafeDispose(ref BigWhiteKeyShader);
            Disposer.SafeDispose(ref BigBarShader);
            Disposer.SafeDispose(ref BigBlackKeyShader);
            Disposer.SafeDispose(ref globalNoteConstants);
            Disposer.SafeDispose(ref noteBuffer);
            Disposer.SafeDispose(ref keyBuffer);
        }

        bool isBlackNote(int n)
        {
            n = n % 12;
            return n == 1 || n == 3 || n == 6 || n == 8 || n == 10;
        }
    }
}
