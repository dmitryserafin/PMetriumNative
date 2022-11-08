﻿using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using InfluxDB.Client.Writes;
using PMetrium.Native.Common.Contracts;
using PMetrium.Native.Common.Helpers;
using PMetrium.Native.Common.Helpers.Extensions;
using Serilog;
using static PMetrium.Native.Common.Helpers.PlatformOSHelper;

namespace PMetrium.Native.Metrics.Android.MetricsHandlers
{
    internal class HardwareMetricsHandler
    {
        private InfluxDBSync _influxDbSync;
        private AndroidDeviceContext _deviceContext;
        private AndroidPerformanceResults _androidPerformanceResults;

        public HardwareMetricsHandler(
            AndroidDeviceContext deviceContext,
            InfluxDBSync influxDbSync,
            AndroidPerformanceResults androidPerformanceResults)
        {
            _influxDbSync = influxDbSync;
            _deviceContext = deviceContext;
            _androidPerformanceResults = androidPerformanceResults;
        }

        public void ExtractAndSaveMetrics(CancellationToken token)
        {
            var device = _deviceContext.DeviceParameters.Device;

            Log.Information($"[Android: {device}] HardwareMetricsHandler - start to handle hardware metrics");

            var tasks = new List<Task>();

            tasks.Add(Task.Run(async () => await ExtractAndSaveCpuMetrics(token)));
            tasks.Add(Task.Run(async () => await ExtractAndSaveRamMetrics(token)));
            tasks.Add(Task.Run(async () => await ExtractAndSaveNetworkMetrics(token)));
            tasks.Add(Task.Run(async () => await ExtractAndSaveBatteryMetrics(token)));
            tasks.Add(Task.Run(async () => await ExtractAndSaveFramesMetrics(token)));

            Task.WaitAll(tasks.ToArray());

            Log.Information($"[Android: {device}] HardwareMetricsHandler - stop to handle hardware metrics");
        }

        private async Task ExtractAndSaveCpuMetrics(CancellationToken token)
        {
            var cpuTotalRaw = (await ExtractDataFromFileOnPhone("cpu_total.txt", token))
                .Replace("%cpu", "", true, CultureInfo.CurrentCulture);
            var cpuUsageTotalRaw = (await ExtractDataFromFileOnPhone("cpu_usage_total.txt", token))
                .Replace("%idle", "", true, CultureInfo.CurrentCulture);
            var cpuUsageAppRaw = await ExtractDataFromFileOnPhone("cpu_usage_app.txt", token);
            var cpuTotal = double.Parse(cpuTotalRaw);
            var points = new List<PointData>();
            var cpuUsageTotalMetrics = ParseOneMetric(cpuUsageTotalRaw.Split("\r\n"));
            var cpuUsageTotalForStatistic = new List<double>();

            foreach (var metric in cpuUsageTotalMetrics)
            {
                var value = Math.Round((cpuTotal - metric.firstMetric) / cpuTotal * 100d, 2);

                points.Add(_influxDbSync.GeneratePoint(
                    "android.cpu.usage.total",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    value,
                    "percentage"));

                cpuUsageTotalForStatistic.Add(value);
            }

            if (cpuUsageTotalForStatistic.Count > 0)
            {
                _androidPerformanceResults.Cpu.TotalCpu_Percentage.Avg = Math.Round(cpuUsageTotalForStatistic.Average(), 2);
                _androidPerformanceResults.Cpu.TotalCpu_Percentage.Min = cpuUsageTotalForStatistic.Min();
                _androidPerformanceResults.Cpu.TotalCpu_Percentage.Max = cpuUsageTotalForStatistic.Max();
            }

            var cpuUsageAppMetrics = ParseOneMetric(cpuUsageAppRaw.Split("\r\n"));
            var cpuUsageApplicationForStatistic = new List<double>();

            foreach (var metric in cpuUsageAppMetrics)
            {
                var value = Math.Round(metric.firstMetric / cpuTotal * 100d, 2);

                points.Add(_influxDbSync.GeneratePoint(
                    "android.cpu.usage.app",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    value,
                    "percentage"));

                cpuUsageApplicationForStatistic.Add(value);
            }

            if (cpuUsageApplicationForStatistic.Count > 0)
            {
                _androidPerformanceResults.Cpu.ApplicationCpu_Percentage.Avg =
                    Math.Round(cpuUsageApplicationForStatistic.Average(), 2);
                _androidPerformanceResults.Cpu.ApplicationCpu_Percentage.Min = cpuUsageApplicationForStatistic.Min();
                _androidPerformanceResults.Cpu.ApplicationCpu_Percentage.Max = cpuUsageApplicationForStatistic.Max();
            }

            await _influxDbSync.SavePoints(points.ToArray());

            Log.Debug(
                $"[Android: {_deviceContext.DeviceParameters.Device}] HardwareMetricsHandler - stop to handle CPU metrics");
        }

        private async Task ExtractAndSaveRamMetrics(CancellationToken token)
        {
            var ramTotalRaw = await ExtractDataFromFileOnPhone("ram_total.txt", token);
            var ramTotal = double.Parse(ramTotalRaw);
            var ramUsageTotalRaw = await ExtractDataFromFileOnPhone("ram_usage_total.txt", token);
            var ramUsageAppRaw = await ExtractDataFromFileOnPhone("ram_usage_app.txt", token);
            var points = new List<PointData>();

            var ramUsageTotalMetrics = ParseOneMetric(ramUsageTotalRaw.Split("\r\n"));

            if (ramUsageTotalMetrics.Count > 0)
            {
                var ramUsageTotalForStatistic = ramUsageTotalMetrics.Select(x => ramTotal - x.firstMetric * 1024d);
                _androidPerformanceResults.Ram.TotalUsedRam_bytes.Avg = Math.Round(ramUsageTotalForStatistic.Average(), 0);
                _androidPerformanceResults.Ram.TotalUsedRam_bytes.Min = Math.Round(ramUsageTotalForStatistic.Min(), 0);
                _androidPerformanceResults.Ram.TotalUsedRam_bytes.Max = Math.Round(ramUsageTotalForStatistic.Max(), 0);
            }
            
            _androidPerformanceResults.Ram.SystemRam_bytes = ramTotal;

            foreach (var metric in ramUsageTotalMetrics)
            {
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.ram.total",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    ramTotal,
                    "byte"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.ram.usage.total",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    ramTotal - (metric.firstMetric * 1024d),
                    "byte"));
            }

            var ramUsageAppMetrics = ParseTwoMetrics(ramUsageAppRaw.Split("\r\n"));

            if (ramUsageAppMetrics.Count > 0)
            {
                var ramUsageAppPSSForStatistic = ramUsageAppMetrics.Select(x => Math.Round(x.firstMetric * 1024d, 0));
                _androidPerformanceResults.Ram.ApplicationPSSRam_bytes.Avg =
                    Math.Round(ramUsageAppPSSForStatistic.Average(), 0);
                _androidPerformanceResults.Ram.ApplicationPSSRam_bytes.Min =
                    Math.Round(ramUsageAppPSSForStatistic.Min(), 0);
                _androidPerformanceResults.Ram.ApplicationPSSRam_bytes.Max = ramUsageAppPSSForStatistic.Max();

                var ramUsageAppPrivateForStatistic = ramUsageAppMetrics.Select(x => Math.Round(x.secondMetric * 1024d, 0));
                _androidPerformanceResults.Ram.ApplicationPrivateRam_bytes.Avg =
                    Math.Round(ramUsageAppPrivateForStatistic.Average(), 0);
                _androidPerformanceResults.Ram.ApplicationPrivateRam_bytes.Min =
                    Math.Round(ramUsageAppPrivateForStatistic.Min(), 0);
                _androidPerformanceResults.Ram.ApplicationPrivateRam_bytes.Max =
                    Math.Round(ramUsageAppPrivateForStatistic.Max(), 0);
            }

            foreach (var metric in ramUsageAppMetrics)
            {
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.ram.usage.app.pss",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.firstMetric * 1024d,
                    "byte"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.ram.usage.app.private",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.secondMetric * 1024d,
                    "byte"));
            }

            await _influxDbSync.SavePoints(points.ToArray());

            Log.Debug(
                $"[Android: {_deviceContext.DeviceParameters.Device}] HardwareMetricsHandler - stop to handle RAM metrics");
        }

        private async Task ExtractAndSaveNetworkMetrics(CancellationToken token)
        {
            var networkUsageTotalRaw = await ExtractDataFromFileOnPhone("network_usage_total.txt", token);
            var networkUsageAppRaw = await ExtractDataFromFileOnPhone("network_usage_app.txt", token);
            var points = new List<PointData>();
            var networkUsageTotalMetrics = ParseFourMetrics(networkUsageTotalRaw.Split("\r\n"));

            if (networkUsageTotalMetrics.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkTotal.MobileTotal.Total.Rx_bytes =
                    networkUsageTotalMetrics.Select(x => x.firstMetric).Max();
                _androidPerformanceResults.Network.NetworkTotal.MobileTotal.Total.Tx_bytes =
                    networkUsageTotalMetrics.Select(x => x.secondMetric).Max();
                _androidPerformanceResults.Network.NetworkTotal.WiFiTotal.Total.Rx_bytes =
                    networkUsageTotalMetrics.Select(x => x.thirdMetric).Max();
                _androidPerformanceResults.Network.NetworkTotal.WiFiTotal.Total.Tx_bytes =
                    networkUsageTotalMetrics.Select(x => x.fourthMetric).Max();
            }

            var previousMetric = (dateTime: DateTime.MinValue, firstMetric: 0d, secondMetric: 0d, thirdMetric: 0d,
                fourthMetric: 0d);

            var networkSpeedTotalMobileRxForStatistic = new List<double>();
            var networkSpeedTotalMobileTxForStatistic = new List<double>();
            var networkSpeedTotalWiFiRxForStatistic = new List<double>();
            var networkSpeedTotalWiFiTxForStatistic = new List<double>();

            foreach (var metric in networkUsageTotalMetrics)
            {
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.mobile.all.total.tx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.secondMetric,
                    "byte"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.mobile.all.total.rx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.firstMetric,
                    "byte"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.wifi.all.total.tx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.fourthMetric,
                    "byte"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.wifi.all.total.rx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.thirdMetric,
                    "byte"));

                if (previousMetric.dateTime == DateTime.MinValue)
                {
                    points.Add(_influxDbSync.GeneratePoint(
                        $"android.network.mobile.speed.total.tx",
                        _deviceContext.CommonTags,
                        metric.dateTime,
                        metric.secondMetric,
                        "byte"));

                    networkSpeedTotalMobileTxForStatistic.Add(metric.secondMetric);

                    points.Add(_influxDbSync.GeneratePoint(
                        $"android.network.mobile.speed.total.rx",
                        _deviceContext.CommonTags,
                        metric.dateTime,
                        metric.firstMetric,
                        "byte"));

                    networkSpeedTotalMobileRxForStatistic.Add(metric.firstMetric);

                    points.Add(_influxDbSync.GeneratePoint(
                        $"android.network.wifi.speed.total.tx",
                        _deviceContext.CommonTags,
                        metric.dateTime,
                        metric.fourthMetric,
                        "byte"));

                    networkSpeedTotalWiFiTxForStatistic.Add(metric.fourthMetric);

                    points.Add(_influxDbSync.GeneratePoint(
                        $"android.network.wifi.speed.total.rx",
                        _deviceContext.CommonTags,
                        metric.dateTime,
                        metric.thirdMetric,
                        "byte"));

                    networkSpeedTotalWiFiRxForStatistic.Add(metric.thirdMetric);

                    previousMetric.dateTime = metric.dateTime;
                    previousMetric.firstMetric = metric.firstMetric;
                    previousMetric.secondMetric = metric.secondMetric;
                    previousMetric.thirdMetric = metric.thirdMetric;
                    previousMetric.fourthMetric = metric.fourthMetric;

                    continue;
                }

                var deltaTime = (int)(metric.dateTime - previousMetric.dateTime).TotalSeconds;

                if (deltaTime == 0)
                    continue;

                var speed = Math.Round((metric.secondMetric - previousMetric.secondMetric) / deltaTime, 2);
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.mobile.speed.total.tx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    speed,
                    "byte"));
                networkSpeedTotalMobileTxForStatistic.Add(speed);

                speed = Math.Round((metric.firstMetric - previousMetric.firstMetric) / deltaTime, 2);
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.mobile.speed.total.rx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    speed,
                    "byte"));
                networkSpeedTotalMobileRxForStatistic.Add(speed);

                speed = Math.Round((metric.fourthMetric - previousMetric.fourthMetric) / deltaTime, 2);
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.wifi.speed.total.tx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    speed,
                    "byte"));
                networkSpeedTotalWiFiTxForStatistic.Add(speed);

                speed = Math.Round((metric.thirdMetric - previousMetric.thirdMetric) / deltaTime, 2);
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.wifi.speed.total.rx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    speed,
                    "byte"));
                networkSpeedTotalWiFiRxForStatistic.Add(speed);

                previousMetric.dateTime = metric.dateTime;
                previousMetric.firstMetric = metric.firstMetric;
                previousMetric.secondMetric = metric.secondMetric;
                previousMetric.thirdMetric = metric.thirdMetric;
                previousMetric.fourthMetric = metric.fourthMetric;
            }

            if (networkSpeedTotalMobileRxForStatistic.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Total.Rx_bytes_per_sec.Avg =
                    Math.Round(networkSpeedTotalMobileRxForStatistic.Average(), 0);
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Total.Rx_bytes_per_sec.Min =
                    networkSpeedTotalMobileRxForStatistic.Min();
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Total.Rx_bytes_per_sec.Max =
                    networkSpeedTotalMobileRxForStatistic.Max();
            }

            if (networkSpeedTotalMobileTxForStatistic.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Total.Tx_bytes_per_sec.Avg =
                    Math.Round(networkSpeedTotalMobileTxForStatistic.Average(), 0);
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Total.Tx_bytes_per_sec.Min =
                    networkSpeedTotalMobileTxForStatistic.Min();
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Total.Tx_bytes_per_sec.Max =
                    networkSpeedTotalMobileTxForStatistic.Max();
            }

            if (networkSpeedTotalWiFiRxForStatistic.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Total.Rx_bytes_per_sec.Avg =
                    Math.Round(networkSpeedTotalWiFiRxForStatistic.Average(), 0);
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Total.Rx_bytes_per_sec.Min =
                    networkSpeedTotalWiFiRxForStatistic.Min();
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Total.Rx_bytes_per_sec.Max =
                    networkSpeedTotalWiFiRxForStatistic.Max();
            }

            if (networkSpeedTotalWiFiTxForStatistic.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Total.Tx_bytes_per_sec.Avg =
                    Math.Round(networkSpeedTotalWiFiTxForStatistic.Average(), 0);
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Total.Tx_bytes_per_sec.Min =
                    networkSpeedTotalWiFiTxForStatistic.Min();
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Total.Tx_bytes_per_sec.Max =
                    networkSpeedTotalWiFiTxForStatistic.Max();
            }

            var networkUsageAppMetrics = ParseFourMetrics(networkUsageAppRaw.Split("\r\n"));
            previousMetric = (dateTime: DateTime.MinValue, firstMetric: 0d, secondMetric: 0d, thirdMetric: 0d,
                fourthMetric: 0d);

            if (networkUsageAppMetrics.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkTotal.MobileTotal.Application.Rx_bytes =
                    networkUsageAppMetrics.Select(x => x.firstMetric).Max();
                _androidPerformanceResults.Network.NetworkTotal.MobileTotal.Application.Tx_bytes =
                    networkUsageAppMetrics.Select(x => x.secondMetric).Max();
                _androidPerformanceResults.Network.NetworkTotal.WiFiTotal.Application.Rx_bytes =
                    networkUsageAppMetrics.Select(x => x.thirdMetric).Max();
                _androidPerformanceResults.Network.NetworkTotal.WiFiTotal.Application.Tx_bytes =
                    networkUsageAppMetrics.Select(x => x.fourthMetric).Max();
            }
                
            var networkSpeedApplicationMobileRxForStatistic = new List<double>();
            var networkSpeedApplicationMobileTxForStatistic = new List<double>();
            var networkSpeedApplicationWiFiRxForStatistic = new List<double>();
            var networkSpeedApplicationWiFiTxForStatistic = new List<double>();

            foreach (var metric in networkUsageAppMetrics)
            {
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.mobile.all.app.tx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.secondMetric,
                    "byte"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.mobile.all.app.rx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.firstMetric,
                    "byte"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.wifi.all.app.tx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.fourthMetric,
                    "byte"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.wifi.all.app.rx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.thirdMetric,
                    "byte"));

                if (previousMetric.dateTime == DateTime.MinValue)
                {
                    points.Add(_influxDbSync.GeneratePoint(
                        $"android.network.mobile.speed.app.tx",
                        _deviceContext.CommonTags,
                        metric.dateTime,
                        metric.secondMetric,
                        "byte"));

                    networkSpeedApplicationMobileTxForStatistic.Add(metric.secondMetric);

                    points.Add(_influxDbSync.GeneratePoint(
                        $"android.network.mobile.speed.app.rx",
                        _deviceContext.CommonTags,
                        metric.dateTime,
                        metric.firstMetric,
                        "byte"));

                    networkSpeedApplicationMobileRxForStatistic.Add(metric.firstMetric);

                    points.Add(_influxDbSync.GeneratePoint(
                        $"android.network.wifi.speed.app.tx",
                        _deviceContext.CommonTags,
                        metric.dateTime,
                        metric.fourthMetric,
                        "byte"));

                    networkSpeedApplicationWiFiTxForStatistic.Add(metric.fourthMetric);

                    points.Add(_influxDbSync.GeneratePoint(
                        $"android.network.wifi.speed.app.rx",
                        _deviceContext.CommonTags,
                        metric.dateTime,
                        metric.thirdMetric,
                        "byte"));

                    networkSpeedApplicationWiFiRxForStatistic.Add(metric.thirdMetric);

                    previousMetric.dateTime = metric.dateTime;
                    previousMetric.firstMetric = metric.firstMetric;
                    previousMetric.secondMetric = metric.secondMetric;
                    previousMetric.thirdMetric = metric.thirdMetric;
                    previousMetric.fourthMetric = metric.fourthMetric;

                    continue;
                }

                var deltaTime = (int)(metric.dateTime - previousMetric.dateTime).TotalSeconds;

                if (deltaTime == 0)
                    continue;

                var speed = Math.Round((metric.secondMetric - previousMetric.secondMetric) / deltaTime, 2);
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.mobile.speed.app.tx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    speed,
                    "byte"));
                networkSpeedApplicationMobileTxForStatistic.Add(speed);

                speed = Math.Round((metric.firstMetric - previousMetric.firstMetric) / deltaTime, 2);
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.mobile.speed.app.rx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    speed,
                    "byte"));
                networkSpeedApplicationMobileRxForStatistic.Add(speed);

                speed = Math.Round((metric.fourthMetric - previousMetric.fourthMetric) / deltaTime, 2);
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.wifi.speed.app.tx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    speed,
                    "byte"));
                networkSpeedApplicationWiFiTxForStatistic.Add(speed);

                speed = Math.Round((metric.thirdMetric - previousMetric.thirdMetric) / deltaTime, 2);
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.network.wifi.speed.app.rx",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    speed,
                    "byte"));
                networkSpeedApplicationWiFiRxForStatistic.Add(speed);

                previousMetric.dateTime = metric.dateTime;
                previousMetric.firstMetric = metric.firstMetric;
                previousMetric.secondMetric = metric.secondMetric;
                previousMetric.thirdMetric = metric.thirdMetric;
                previousMetric.fourthMetric = metric.fourthMetric;
            }

            if (networkSpeedApplicationMobileRxForStatistic.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Application.Rx_bytes_per_sec.Avg =
                    Math.Round(networkSpeedApplicationMobileRxForStatistic.Average(), 0);
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Application.Rx_bytes_per_sec.Min =
                    networkSpeedApplicationMobileRxForStatistic.Min();
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Application.Rx_bytes_per_sec.Max =
                    networkSpeedApplicationMobileRxForStatistic.Max();
            }

            if (networkSpeedApplicationMobileTxForStatistic.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Application.Tx_bytes_per_sec.Avg =
                    Math.Round(networkSpeedApplicationMobileTxForStatistic.Average(), 0);
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Application.Tx_bytes_per_sec.Min =
                    networkSpeedApplicationMobileTxForStatistic.Min();
                _androidPerformanceResults.Network.NetworkSpeed.MobileSpeed.Application.Tx_bytes_per_sec.Max =
                    networkSpeedApplicationMobileTxForStatistic.Max();
            }

            if (networkSpeedApplicationWiFiRxForStatistic.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Application.Rx_bytes_per_sec.Avg =
                    Math.Round(networkSpeedApplicationWiFiRxForStatistic.Average(), 0);
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Application.Rx_bytes_per_sec.Min =
                    networkSpeedApplicationWiFiRxForStatistic.Min();
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Application.Rx_bytes_per_sec.Max =
                    networkSpeedApplicationWiFiRxForStatistic.Max();
            }

            if (networkSpeedApplicationWiFiTxForStatistic.Count > 0)
            {
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Application.Tx_bytes_per_sec.Avg =
                    Math.Round(networkSpeedApplicationWiFiTxForStatistic.Average(), 0);
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Application.Tx_bytes_per_sec.Min =
                    networkSpeedApplicationWiFiTxForStatistic.Min();
                _androidPerformanceResults.Network.NetworkSpeed.WiFiSpeed.Application.Tx_bytes_per_sec.Max =
                    networkSpeedApplicationWiFiTxForStatistic.Max();
            }

            await _influxDbSync.SavePoints(points.ToArray());

            Log.Debug(
                $"[Android: {_deviceContext.DeviceParameters.Device}] HardwareMetricsHandler - stop to handle NETWORK metrics");
        }

        private async Task ExtractAndSaveBatteryMetrics(CancellationToken token)
        {
            var batteryUsageAppRaw = await ExtractDataFromFileOnPhone("battery_app.txt", token);
            var batteryUsageAppMetrics = ParseOneMetric(batteryUsageAppRaw.Split("\r\n"));
            var points = new List<PointData>();

            if (batteryUsageAppMetrics.Count > 0)
            {
                var batteryUsageAppForStatistic = batteryUsageAppMetrics.Select(x => x.firstMetric);
                _androidPerformanceResults.Battery.Application_mAh = batteryUsageAppForStatistic.Max();
            }

            foreach (var metric in batteryUsageAppMetrics)
            {
                points.Add(_influxDbSync.GeneratePoint(
                    "android.battery.usage.app",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.firstMetric,
                    "mAh"));
            }

            await _influxDbSync.SavePoints(points.ToArray());

            Log.Debug(
                $"[Android: {_deviceContext.DeviceParameters.Device}] HardwareMetricsHandler - stop to handle BATTERY metrics");
        }

        private async Task ExtractAndSaveFramesMetrics(CancellationToken token)
        {
            var framesAppRaw = await ExtractDataFromFileOnPhone("frames_app.txt", token);
            var framesAppMetrics = ParseFramesMetrics(framesAppRaw
                .Split(new[] { "Total" }, StringSplitOptions.RemoveEmptyEntries));
            var points = new List<PointData>();

            if (framesAppMetrics.Count > 0)
            {
                var renderedFramesAppForStatistic = framesAppMetrics.Select(x => x.firstMetric);
                _androidPerformanceResults.Frames.ApplicationRenderedFrames = renderedFramesAppForStatistic.Max();

                var jankyFramesAppForStatistic = framesAppMetrics.Select(x => x.secondMetric);
                _androidPerformanceResults.Frames.ApplicationJankyFrames = jankyFramesAppForStatistic.Max();
            }

            foreach (var metric in framesAppMetrics)
            {
                points.Add(_influxDbSync.GeneratePoint(
                    $"android.frames.rendered",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.firstMetric,
                    "count"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.frames.janky",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.secondMetric,
                    "count"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.frames.rendering",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.thirdMetric,
                    "pct50"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.frames.rendering",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.fourthMetric,
                    "pct90"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.frames.rendering",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.fifthMetric,
                    "pct95"));

                points.Add(_influxDbSync.GeneratePoint(
                    $"android.frames.rendering",
                    _deviceContext.CommonTags,
                    metric.dateTime,
                    metric.sixthMetric,
                    "pct99"));
            }

            await _influxDbSync.SavePoints(points.ToArray());

            Log.Debug(
                $"[Android: {_deviceContext.DeviceParameters.Device}] HardwareMetricsHandler - stop to handle FRAMES metrics");
        }

        private List<(
            DateTime dateTime,
            double firstMetric,
            double secondMetric,
            double thirdMetric,
            double fourthMetric,
            double fifthMetric,
            double sixthMetric)> ParseFramesMetrics(string[] metricsRaw)
        {
            var result = new List<(
                DateTime dateTime,
                double firstMetric,
                double secondMetric,
                double thirdMetric,
                double fourthMetric,
                double fifthMetric,
                double sixthMetric)>();

            foreach (var line in metricsRaw)
            {
                var splitResult = line.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                var metricsList = splitResult.Where(x =>
                    x.Contains("frames rendered") ||
                    x.Contains("Janky frames") &&
                    !x.Contains("(legacy)") ||
                    x.Contains("50th percentile") ||
                    x.Contains("90th percentile") ||
                    x.Contains("95th percentile") ||
                    x.Contains("99th percentile") ||
                    long.TryParse(x, out _)).ToList();

                if (metricsList.Count != 7) continue;

                var timestamp = metricsList.Single(x => long.TryParse(x, out _));
                var dateTime = DateTime.UnixEpoch.AddSeconds(long.Parse(timestamp));

                var firstMetric = double.Parse(
                    metricsList.Single(x => x.Contains("frames rendered"))
                        .Replace("frames rendered: ", ""),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture);

                var secondMetric = metricsList
                    .Single(x => x.Contains("Janky frames"))
                    .Replace("Janky frames: ", "");
                secondMetric = Regex.Replace(secondMetric, "\\(\\d+.\\d+%\\)", "");

                var thirdMetric = double.Parse(metricsList
                    .Single(x => x.Contains("50th percentile"))
                    .Replace("50th percentile: ", "")
                    .Replace("ms", ""), NumberStyles.Any, CultureInfo.InvariantCulture);

                var fourthMetric = double.Parse(metricsList
                    .Single(x => x.Contains("90th percentile"))
                    .Replace("90th percentile: ", "")
                    .Replace("ms", ""), NumberStyles.Any, CultureInfo.InvariantCulture);

                var fifthMetric = double.Parse(metricsList
                    .Single(x => x.Contains("95th percentile"))
                    .Replace("95th percentile: ", "")
                    .Replace("ms", ""), NumberStyles.Any, CultureInfo.InvariantCulture);

                var sixthMetric = double.Parse(metricsList
                    .Single(x => x.Contains("99th percentile"))
                    .Replace("99th percentile: ", "")
                    .Replace("ms", ""), NumberStyles.Any, CultureInfo.InvariantCulture);

                result.Add((dateTime, firstMetric, double.Parse(secondMetric), thirdMetric, fourthMetric, fifthMetric,
                    sixthMetric));
            }

            return result;
        }

        private async Task<string> ExtractDataFromFileOnPhone(string fileName, CancellationToken token)
        {
            var result = "";
            if (!token.IsCancellationRequested)
            {
                var process = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? CreateProcess($"{WorkingDirectory}\\Scripts\\Bat\\readFile.bat",
                        $"{_deviceContext.DeviceParameters.Device} {fileName}")
                    : CreateProcess($"{WorkingDirectory}/Scripts/Shell/readFile.sh",
                        $"{_deviceContext.DeviceParameters.Device} {fileName}");

                process.OutputDataReceived += (sender, outLine) =>
                {
                    if (!string.IsNullOrEmpty(outLine.Data))
                        result += outLine.Data + "\r\n";
                };

                process.StartForDevice(_deviceContext.DeviceParameters.Device);
                await process.WaitForExitAsync(token);
            }

            return result;
        }

        private List<(DateTime dateTime, double firstMetric)> ParseOneMetric(string[] metricsRaw)
        {
            var result = new List<(DateTime dateTime, double firstMetric)>();

            foreach (var line in metricsRaw)
            {
                var splitResult = line.Split("_");

                if (splitResult.Length != 2) continue;

                var dateTime = DateTime.UnixEpoch.AddSeconds(long.Parse(splitResult[0]));
                var firstMetric = double.Parse(splitResult[1], NumberStyles.Any, CultureInfo.InvariantCulture);

                result.Add((dateTime, firstMetric));
            }

            return result;
        }

        private List<(DateTime dateTime, double firstMetric, double secondMetric)> ParseTwoMetrics(
            string[] metricsRaw)
        {
            var result = new List<(DateTime dateTime, double firstMetric, double secondMetric)>();

            foreach (var line in metricsRaw)
            {
                var splitResult = line.Split("_");

                if (splitResult.Length != 3) continue;

                var dateTime = DateTime.UnixEpoch.AddSeconds(long.Parse(splitResult[0]));
                var firstMetric = double.Parse(splitResult[1], NumberStyles.Any, CultureInfo.InvariantCulture);
                var secondMetric = double.Parse(splitResult[2], NumberStyles.Any, CultureInfo.InvariantCulture);

                result.Add((dateTime, firstMetric, secondMetric));
            }

            return result;
        }

        private List<(DateTime dateTime, double firstMetric, double secondMetric, double thirdMetric, double
            fourthMetric
            )> ParseFourMetrics(
            string[] metricsRaw)
        {
            var result =
                new List<(DateTime dateTime, double firstMetric, double secondMetric, double thirdMetric, double
                    fourthMetric)>();

            foreach (var line in metricsRaw)
            {
                var splitResult = line.Split("_");

                if (splitResult.Length != 5) continue;

                var dateTime = DateTime.UnixEpoch.AddSeconds(long.Parse(splitResult[0]));
                var firstMetric = double.Parse(splitResult[1], NumberStyles.Any, CultureInfo.InvariantCulture);
                var secondMetric = double.Parse(splitResult[2], NumberStyles.Any, CultureInfo.InvariantCulture);
                var thirdMetric = double.Parse(splitResult[3], NumberStyles.Any, CultureInfo.InvariantCulture);
                var fourthMetric = double.Parse(splitResult[4], NumberStyles.Any, CultureInfo.InvariantCulture);

                result.Add((dateTime, firstMetric, secondMetric, thirdMetric, fourthMetric));
            }

            return result;
        }
    }
}