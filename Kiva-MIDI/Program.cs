﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KivaShared;
using System.IO;

namespace Kiva_MIDI
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        { 
            var window = new MainWindow();

            KivaUpdates.InstallFromStream(File.OpenRead("Release.zip"));

            try
            {
                if (args.Length != 0)
                {
                    window.LoadMidi(args[0]);
                }
                window.ShowDialog();
            }
            catch (Exception e)
            {
                string msg = e.Message + "\n" + e.Data + "\n";
                msg += e.StackTrace;
                MessageBox.Show(msg, "Kiva has crashed!");
            }
        }
    }
}