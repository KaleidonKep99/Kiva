﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Kiva.Audio.APIs;
using Sanford.Multimedia.Midi;

namespace Kiva.Settings.Views
{
    /// <summary>
    /// Interaction logic for AudioSettings.xaml
    /// </summary>
    public partial class AudioSettings : UserControl
    {
        private KivaSettings settings;

        public KivaSettings Settings
        {
            get => settings; set
            {
                settings = value;
                SetValues();
                winmmSettings.Settings = settings;
                prerenderSettings.Settings = settings;
            }
        }

        SolidColorBrush selectBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));

        bool kdmapiAvailable = false;

        public AudioSettings()
        {
            InitializeComponent();
            try
            {
                kdmapiAvailable = KDMAPI.IsKDMAPIAvailable();
            }
            catch { }
            if (!kdmapiAvailable) kdmapiEngine.Visibility = Visibility.Collapsed;
            kdmapiEngine.PreviewMouseDown += (s, e) => { settings.General.SelectedAudioEngine = AudioEngine.KDMAPI; SetValues(); };
            winmmEngine.PreviewMouseDown += (s, e) => { settings.General.SelectedAudioEngine = AudioEngine.WinMM; SetValues(); };
            prerenderEngine.PreviewMouseDown += (s, e) => { settings.General.SelectedAudioEngine = AudioEngine.PreRender; SetValues(); };
        }

        void Deselect()
        {
            kdmapiEngine.Background = Brushes.Transparent;
            winmmEngine.Background = Brushes.Transparent;
            prerenderEngine.Background = Brushes.Transparent;

            kdmapiSettings.Visibility = Visibility.Collapsed;
            winmmSettings.Visibility = Visibility.Collapsed;
            prerenderSettings.Visibility = Visibility.Collapsed;
        }

        public void SetValues()
        {
            Deselect();
            switch (settings.General.SelectedAudioEngine)
            {
                case AudioEngine.KDMAPI:
                    kdmapiEngine.Background = selectBrush;
                    kdmapiSettings.Visibility = Visibility.Visible;
                    break;
                case AudioEngine.WinMM:
                    winmmEngine.Background = selectBrush;
                    winmmSettings.Visibility = Visibility.Visible;
                    break;
                case AudioEngine.PreRender:
                    prerenderEngine.Background = selectBrush;
                    prerenderSettings.Visibility = Visibility.Visible;
                    break;
            }
        }
    }
}
