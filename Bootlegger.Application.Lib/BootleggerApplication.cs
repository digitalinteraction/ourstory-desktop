﻿using Docker.DotNet;
using Docker.DotNet.Models;
using Rssdp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bootlegger.App.Lib
{
    public class BootleggerApplication:IProgress<JSONMessage>
    {
        public enum RUNNING_STATE { NOT_SUPORTED, NO_DOCKER, NO_DOCKER_RUNNING, NO_IMAGES, READY, RUNNING }

        public RUNNING_STATE CurrentState { get; private set; }
        public OperatingSystem CurrentPlatform { get; private set; }

        public BootleggerApplication()
        {
            CurrentPlatform = System.Environment.OSVersion;
        }

        Docker.DotNet.DockerClient dockerclient;

        private SsdpDevicePublisher _Publisher;

        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("Local IP Address Not Found!");
        }

        public async Task Start()
        {
            _Publisher = new SsdpDevicePublisher();
            var deviceDefinition = new SsdpRootDevice()
            {
                CacheLifetime = TimeSpan.FromMinutes(30), //How long SSDP clients can cache this info.
                Location = new Uri("http://"+ Dns.GetHostName()), // Must point to the URL that serves your devices UPnP description document. 
                DeviceTypeNamespace = "bootlegger",
                DeviceType = "server",
                DeviceVersion = 1,
                FriendlyName = "Bootlegger Server",
                Manufacturer = "Newcastle University",
                ModelName = "Serverv1",
                Uuid = Guid.NewGuid().ToString() // This must be a globally unique value that survives reboots etc. Get from storage or embedded hardware etc.
            };
            _Publisher.AddDevice(deviceDefinition);

            if (CurrentPlatform.Platform == PlatformID.Win32Windows || CurrentPlatform.Platform == PlatformID.Unix)
            {
                CurrentState = RUNNING_STATE.NOT_SUPORTED;
                return;
            }

            try
            { 
                await CheckDocker();
            }
            catch (Exception e)
            {
                CurrentState = RUNNING_STATE.NO_DOCKER;
            }                

            if (CurrentState == RUNNING_STATE.NO_IMAGES)
            {
                await Task.Run(() =>
                {
                    //start docker connection
                    switch (CurrentPlatform.Platform)
                    {
                        case PlatformID.Win32NT:
                            dockerclient = new DockerClientConfiguration(new Uri("npipe://./pipe/docker_engine")).CreateClient();
                            break;
                    }
                });

                Task containers = dockerclient.Containers.ListContainersAsync(new Docker.DotNet.Models.ContainersListParameters() { All = true });
                if (await Task.WhenAny(containers, Task.Delay(10000)) == containers)
                {
                    //containers installed?
                    try
                    {
                        var exists = await dockerclient.Images.InspectImageAsync("bootlegger/server");
                        CurrentState = RUNNING_STATE.READY;
                    }
                    catch (Exception e)
                    {
                        CurrentState = RUNNING_STATE.NO_IMAGES;
                    }
                }
                else
                {
                    // timeout logic
                    CurrentState = RUNNING_STATE.NO_DOCKER_RUNNING;
                }                
            }
        }

        internal void OpenDownloadLink()
        {
            System.Diagnostics.Process.Start(DockerLink);
        }

        public async Task CheckDocker()
        {
            await Task.Run(() =>
            {
                Process p = new Process();
                p.StartInfo = new ProcessStartInfo()
                {
                    FileName = "docker",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                p.Start();
                p.WaitForExit();

                p = new Process();
                p.StartInfo = new ProcessStartInfo()
                {
                    FileName = "docker-compose",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                p.Start();
                p.WaitForExit();

                p = new Process();
                p.StartInfo = new ProcessStartInfo()
                {
                    FileName = "docker",
                    Arguments = "info",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                p.Start();
                p.WaitForExit();
                if (p.ExitCode == 0)
                    CurrentState = RUNNING_STATE.NO_IMAGES;
                else
                {
                    CurrentState = RUNNING_STATE.NO_DOCKER_RUNNING;
                }
            });
        }

        internal void OpenFolder()
        {
            System.Diagnostics.Process.Start(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "media");
        }

        public string DockerLink
        {
            get
            {
                switch (CurrentPlatform.Platform)
                {
                    case PlatformID.Win32NT:
                        return "https://store.docker.com/editions/community/docker-ce-desktop-windows";
                    case PlatformID.MacOSX:
                        return "https://store.docker.com/editions/community/docker-ce-desktop-mac";
                    default:
                        return "https://store.docker.com";
                }
            }
        }

        public void OpenAdminPanel()
        {
            System.Diagnostics.Process.Start("http://localhost");
        }

        List<ImagesCreateParameters> imagestodownload;
        private int CurrentDownload = 0;

        public async Task DownloadImages(bool forceupdate, CancellationToken cancel)
        {
            CurrentDownload = 1;

            imagestodownload = new List<ImagesCreateParameters>();

            //download images needed
            imagestodownload.Add(new ImagesCreateParameters()
            {
                FromImage = "mvertes/alpine-mongo",
                Tag = "latest"
            });

            imagestodownload.Add(new ImagesCreateParameters()
            {
                FromImage = "redis",
                Tag = "alpine"
            });

            imagestodownload.Add(new ImagesCreateParameters()
            {
                FromImage = "jwilder/docker-gen",
                Tag = "latest"
            });

            imagestodownload.Add(new ImagesCreateParameters()
            {
                FromImage = "kusmierz/beanstalkd",
                Tag = "latest"
            });

            imagestodownload.Add(new ImagesCreateParameters()
            {
                FromImage = "bootlegger/transcode-server",
                Tag = "latest"
            });

            imagestodownload.Add(new ImagesCreateParameters()
            {
                FromImage = "nginx:alpine"
            });

            //imagestodownload.Add(new ImagesCreateParameters()
            //{
            //    FromImage = "jwilder/docker-gen"
            //});

            imagestodownload.Add(new ImagesCreateParameters()
            {
                FromImage = "bootlegger/server",
                Tag = "latest"
            });

            List<Task> tasks = new List<Task>();

            foreach(var im in imagestodownload)
            {
                if (!cancel.IsCancellationRequested)
                {
                    Layers.Clear();

                    //detect if it exists:
                    try
                    {
                        if (forceupdate)
                            throw new Exception("Must update");
                        var exists = await dockerclient.Images.InspectImageAsync(im.FromImage, cancel);
                        CurrentDownload++;
                        OnNextDownload(CurrentDownload, imagestodownload.Count, CurrentDownload / (double)imagestodownload.Count);
                    }
                    catch
                    {
                        await dockerclient.Images.CreateImageAsync(im, null, this, cancel);
                        CurrentDownload++;
                        OnNextDownload(CurrentDownload, imagestodownload.Count, CurrentDownload / (double)imagestodownload.Count);
                    }
                }
                else
                    break;
            }
        }


        private async Task StartContainer(CreateContainerParameters param)
        {
            try
            {
                //check if containers are running:
                var spec = await dockerclient.Containers.InspectContainerAsync(param.Name);
                await dockerclient.Containers.StartContainerAsync(spec.ID, null);
            }
            catch
            {
                await dockerclient.Containers.CreateContainerAsync(param);
            }
        }

        private async Task<VolumeResponse> CreateVolume(VolumesCreateParameters param)
        {
            try
            {
                return await dockerclient.Volumes.InspectAsync(param.Name);
            }
            catch
            {
                return await dockerclient.Volumes.CreateAsync(param);
            }
        }

        //public void CreateWiFi(string ssid, string pwd)
        //{
        //    if (CurrentPlatform.Platform == PlatformID.Win32NT)
        //    {
        //        Process.Start(new ProcessStartInfo()
        //        {
        //            FileName = "netsh",
        //            Arguments = "wlan set hostednetwork mode = allow ssid = "+ssid+" key = "+pwd,
        //            UseShellExecute = false,
        //            CreateNoWindow = true
        //        }    
        //        );
        //        //netsh wlan set hostednetwork mode = allow ssid = Hotspot key = 7Tutorials
        //        Process.Start(new ProcessStartInfo()
        //        {
        //            FileName = "netsh",
        //            Arguments = "wlan start hostednetwork",
        //            UseShellExecute = false,
        //            CreateNoWindow = true
        //        }
        //        );
        //        //netsh wlan start hostednetwork
        //    }
        //}

        //public void StopWifi()
        //{
        //    //netsh wlan stop hostednetwork
        //}

        public event Action<string> OnLog;

        //start containers...
        public async Task<bool> RunServer()
        {
            //if not running:
            Process dc = new Process();
            dc.StartInfo = new ProcessStartInfo("docker-compose");
            dc.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            dc.StartInfo.Arguments = "-p bootleggerlocal up -d";
            dc.StartInfo.Environment.Add("MYIP", GetLocalIPAddress());
            dc.StartInfo.UseShellExecute = false;
            dc.StartInfo.CreateNoWindow = true;
            dc.StartInfo.RedirectStandardOutput = true;
            dc.StartInfo.RedirectStandardError = true;

            dc.Start();
            dc.BeginOutputReadLine();
            dc.BeginErrorReadLine();

            dc.OutputDataReceived += (s, o) =>
            {
                OnLog?.Invoke(o.Data);
            };

            dc.ErrorDataReceived += (s, o) =>
            {
                OnLog?.Invoke(o.Data);
            };

            await Task.Run(() =>
            {
                dc.WaitForExit();
            });

            WebClient client = new WebClient();
    
            bool connected = false;
            int count = 0;
            while (!connected && count < 10)
            {
                try
                {
                    var result = await client.DownloadStringTaskAsync($"http://{GetLocalIPAddress()}/status");
                    connected = true;
                }
                catch {
                    await Task.Delay(5000);
                }
                finally
                {
                    count++;
                }
            }

            return dc.ExitCode == 0;
        }

        //stop containers
        public async Task StopServer()
        {
            try
            {
                Process dc = new Process();
                dc.StartInfo = new ProcessStartInfo("docker-compose");
                dc.StartInfo.Arguments = "-p bootleggerlocal stop";
                dc.StartInfo.UseShellExecute = false;
                dc.StartInfo.CreateNoWindow = true;
                dc.Start();
                await Task.Run(() =>
                {
                    dc.WaitForExit();
                });
            }
            catch
            {
                //cant stop?
            }
        }

        //message, current, total, sub, overall
        public event Action<string, int, int, Dictionary<string, double>, double> OnDownloadProgress;
        public event Action<int, int, double> OnNextDownload;

        Dictionary<string, double> Layers = new Dictionary<string, double>();

        public void Report(JSONMessage value)
        {
            //Debug.WriteLine(value.From);

            //Debug.WriteLine(value.Status);
            //Debug.WriteLine(value.ProgressMessage);
            if (value.ProgressMessage != null)
            {
                //Debug.WriteLine(value.ProgressMessage);
                Layers[value.ID] = value.Progress.Current / (double)value.Progress.Total;
                //Console.WriteLine(CurrentDownload);
                OnDownloadProgress?.Invoke(value.Status, CurrentDownload, imagestodownload.Count, Layers, CurrentDownload / (double)imagestodownload.Count);
            }
        }
    }
}
