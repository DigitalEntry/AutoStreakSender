using Google.Apis.Auth.OAuth2;
using Google.Cloud.Vision.V1;
using SharpAdbClient;
using SharpAdbClient.DeviceCommands;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoStreakSender
{
	class Program
	{
		public static void Main(string[] args)
		{
			Console.ResetColor();
			if (!File.Exists($"{Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location)}/Auth.json"))
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Please set the JSON file that contains your service account key in the folder with your exe");
				Console.ResetColor();
				return;
			}

			Console.WriteLine("How many streaks do you have?");
			string AmountOfStreaks = Console.ReadLine();
			Console.WriteLine("What do your streaks start with eg. AA/John");
			string StartOfStreak = Console.ReadLine();

			_ = Task.Run(() => SendStreaks(AmountOfStreaks, StartOfStreak));
			Console.ReadKey();
		}

		public static async Task SendStreaks(string AmountOfStreaks, string StartOfStreak)
		{
			AdbServer server = new AdbServer();
			StartServerResult Result;
			try
			{
				Result = server.StartServer($"{Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location)}\\adb.exe", restartServerIfNewer: false);

				Console.WriteLine($"Server now running on {AdbClient.AdbServerPort}");
			}
			catch
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Failed to start the server");
				Console.ResetColor();
				return;
			}

			var Devices = AdbClient.Instance.GetDevices();

			if (Devices.Count > 1)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("You cannot have more than 1 phone plugged in");
				Console.ResetColor();
				return;
			}

			Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", $"{Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location)}/Auth.json");
			ImageAnnotatorClient client = ImageAnnotatorClient.Create();

			int StreaksToSend = 0;
			try
			{
				StreaksToSend = int.Parse(AmountOfStreaks);
			}
			catch
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Failed to parse AmountOfStreaks");
				Console.ResetColor();
				return;
			}

			List<string> Names = new List<string>();
			int StreaksSent = 0;

			DeviceData Device = Devices[0];
			var GetSizeReceiver = new ConsoleOutputReceiver();

			Device.ExecuteShellCommand($"wm size", GetSizeReceiver);
			string[] lines = GetSizeReceiver.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
			string DeviceSize = lines[1];
			DeviceSize = DeviceSize.Replace("Override size: ", string.Empty);
			string[] Split = DeviceSize.Split("x");
			string Width = Split[0];
			string Height = Split[1];

			Device.ExecuteShellCommand($"input tap {int.Parse(Width) / 2} {int.Parse(Height) - 150}", GetSizeReceiver);
			Thread.Sleep(150);
			Device.ExecuteShellCommand($"input tap {(int.Parse(Width) / 2) - 50} {int.Parse(Height) - 290}", GetSizeReceiver);
			Thread.Sleep(1200);
			Device.ExecuteShellCommand($"input tap {int.Parse(Width) - 50} {int.Parse(Height) - 100}", GetSizeReceiver);
			Thread.Sleep(500);

			for (int i = 0; i <= 20; i++)
			{
				var Receiver = new ConsoleOutputReceiver();
				string RandomFileName = GenerateRandomString(25);

				Device.ExecuteShellCommand($"exec screencap -p > /storage/emulated/0/Pictures/{RandomFileName}.png", Receiver);

				if (string.IsNullOrWhiteSpace(Receiver.ToString()))
				{
					Console.WriteLine("Screenshot taken");
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine(Receiver.ToString());
					Console.ResetColor();
					return;
				}

				using (SyncService service = new SyncService(new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)), Device))
				using (Stream stream = File.OpenWrite(@$"{Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location)}/Screenshots/{RandomFileName}.png"))
				{
					service.Pull($"/storage/emulated/0/Pictures/{RandomFileName}.png", stream, null, CancellationToken.None);
				}

				Device.ExecuteShellCommand($"rm /storage/emulated/0/Pictures/{RandomFileName}.png", Receiver);
				if (!string.IsNullOrWhiteSpace(Receiver.ToString()))
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine("Failed to delete screenshot on phone while moving it locally");
					Console.ResetColor();
					return;
				}

				var image = Google.Cloud.Vision.V1.Image.FromFile(@$"{Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location)}/Screenshots/{RandomFileName}.png");
				var response = client.DetectText(image);

				foreach (var annotation in response)
				{
					if (annotation != null)
					{
						if (annotation.Description.StartsWith(StartOfStreak))
						{
							if (annotation.BoundingPoly.Vertices[0].Y <= 2100)
							{
								if (!Names.Contains(annotation.Description))
								{
									if (StreaksSent >= StreaksToSend)
									{
										i = 21;
										break;
									}
									Console.WriteLine(annotation.Description);
									Names.Add(annotation.Description);
									Device.ExecuteShellCommand($"input tap {annotation.BoundingPoly.Vertices[0].X} {annotation.BoundingPoly.Vertices[0].Y}", Receiver);
									StreaksSent++;
								}
							}
						}
					}
					Thread.Sleep(50);
				}
				Device.ExecuteShellCommand($"input swipe 250 1100 250 400 300", Receiver);
				Thread.Sleep(950);
			}

			Console.WriteLine("Streaks have been selected, Make sure the application didn't skip any1");
		}

		public static string GenerateRandomString(int length)
		{
			const string valid = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
			StringBuilder res = new StringBuilder();
			Random rnd = new Random();
			while (0 < length--)
			{
				res.Append(valid[rnd.Next(valid.Length)]);
			}
			return res.ToString();
		}
	}
}
