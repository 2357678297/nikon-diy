using System;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CameraControl.Core;
using CameraControl.Core.Classes;
using CameraControl.Core.Interfaces;
using ImageMagick;

using System;

namespace CameraControl.Plugins.ImageTransformPlugins
{
    public class CropTransform : IImageTransformPlugin
    {
        public string Name
        {
            get { return "Crop"; }
        }

        public string Execute(FileItem item, string infile, string dest, ValuePairEnumerator configData)
        {
            var conf = new CropTransformViewModel(configData);
            using (MagickImage image = new MagickImage(infile))
            {
                if (conf.FromLiveView && ServiceProvider.DeviceManager.SelectedCameraDevice != null)
                {
                    var prop = ServiceProvider.DeviceManager.SelectedCameraDevice.LoadProperties();
                    conf.Left = (int)(image.Width*prop.LiveviewSettings.HorizontalMin/1000);
                    conf.Width = (int)(image.Width*(prop.LiveviewSettings.HorizontalMax)/1000);
                    conf.Top = (int)(image.Height*prop.LiveviewSettings.VerticalMin/1000);
                    conf.Height = (int)(image.Height*(prop.LiveviewSettings.VerticalMax)/1000);
                }
                if (conf.CropMargins)
                {
                    conf.Left = (int)(image.Width * conf.WidthProcent / 100);
                    conf.Width = (int)(image.Width - (conf.Left*2));
                    conf.Top = (int)(image.Height * conf.HeightProcent / 100);
                    conf.Height = (int)(image.Height - (conf.Top*2));
                }

                if (conf.LiveViewCrop)
                {
                    var prop = ServiceProvider.DeviceManager.SelectedCameraDevice.LoadProperties();
                    conf.Left = (int)(image.Width/2*prop.LiveviewSettings.CropRatio/100);
                    conf.Width = (int)(image.Width - (conf.Left*2));
                    conf.Top = (int)(image.Height/2*prop.LiveviewSettings.CropRatio/100);
                    conf.Height = (int)(image.Height - (conf.Top*2));
                }

                MagickGeometry geometry = new MagickGeometry();
                geometry.Width = (uint)Math.Max(0, conf.Width);
                geometry.Height = (uint)Math.Max(0, conf.Height);
                geometry.X = conf.Left;
                geometry.Y = conf.Top;
                image.Crop(geometry);
              
                if(conf.Trim)
                {
                    image.ColorFuzz = new Percentage(conf.TrimSensitivity);
                    image.Trim();
                    image.ResetPage();
                }                
                image.Format = MagickFormat.Jpeg;
                image.Write(dest);
            }
            return dest;
        }

        public UserControl GetConfig(ValuePairEnumerator configData)
        {
            var control = new CropTransformView {DataContext = new CropTransformViewModel(configData)};
            return control;
        }
    }
}
