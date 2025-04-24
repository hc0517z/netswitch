using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using Spectre.Console;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace netswitch
{
    public static class Program
    {
        private static readonly List<NetworkProfile> Profiles = LoadProfilesFromYaml("profiles.yaml");
        
        private static void Main()
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                AnsiConsole.MarkupLine("[red]이 프로그램을 실행하려면이 응용 프로그램을 관리자로 실행해야합니다.[/]");
                AnsiConsole.MarkupLine("프로그램을 종료하려면 아무키나 누르세요.");
                Console.ReadKey();
                return;
            }
            
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // CP949 지원 활성화
            Console.OutputEncoding = Encoding.UTF8; // 콘솔 출력은 UTF-8 유지
            
            AnsiConsole.MarkupLine("[bold yellow]활성화된 유선 네트워크 목록:[/]");

            var adapters = GetActiveEthernetAdapters();
            if (adapters.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]활성화된 유선 네트워크가 없습니다.[/]");
                return;
            }

            var selectedAdapter = AnsiConsole.Prompt(
                new SelectionPrompt<NetworkInterface>()
                    .Title("설정을 적용할 네트워크 어댑터를 선택하세요(방향키, 엔터):")
                    .PageSize(10)
                    .UseConverter(a => $"{a.Name} - {a.Description}")
                    .AddChoices(adapters)
            );

            AnsiConsole.MarkupLine($"[green]{selectedAdapter.Name} 선택됨[/]");

            var selectedProfile = AnsiConsole.Prompt(
                new SelectionPrompt<NetworkProfile>()
                    .Title("적용할 네트워크 프로파일을 선택하세요(방향키, 엔터):")
                    .PageSize(10)
                    .UseConverter(p => $"{p.Name} ({p.Ip})")
                    .AddChoices(Profiles)
            );

            AnsiConsole.MarkupLine($"[green]{selectedProfile.Name} 프로파일 선택됨[/]");
            ApplyNetworkSettings(selectedAdapter.Name, selectedProfile);
            
            AnsiConsole.MarkupLine("[yellow]네트워크 어댑터 간 IP 충돌 시 정상적으로 적용이 안될 수 있습니다.[/]");
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[blue]네트워크 연결 확인: 실행(Win+R) - ncpa.cpl 입력[/]");
            AnsiConsole.MarkupLine("[bold]스페이스바를 입력하면 프로그램이 종료됩니다...[/]");
        
            // 스페이스바 입력 대기
            while (true)
            {
                // 키 입력 대기
                var key = Console.ReadKey(intercept: true); // 입력된 키를 화면에 표시하지 않음
                if (key.Key == ConsoleKey.Spacebar) // 스페이스바가 입력되었을 경우
                {
                    break; // 반복문을 종료하고 프로그램 종료
                }
            }
        }

        static List<NetworkInterface> GetActiveEthernetAdapters()
        {
            List<NetworkInterface> activeAdapters = new();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                    adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.GetIPProperties().UnicastAddresses
                        .Any(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    activeAdapters.Add(adapter);
                }
            }

            return activeAdapters;
        }

        static void ApplyNetworkSettings(string adapterName, NetworkProfile profile)
        {
            AnsiConsole.Status().Start("네트워크 설정 적용 중...", ctx =>
            {
                ctx.Status = $"IP 및 서브넷 마스크 설정 중... (ip:{profile.Ip}, subnet:{profile.SubnetMask}, gateway:{profile.Gateway})";
                RunNetshCommand($"interface ipv4 set address \"{adapterName}\" static {profile.Ip} {profile.SubnetMask} {profile.Gateway}");

                ctx.Status = "기존 DNS 서버 삭제 중...";
                RunNetshCommand($"interface ipv4 delete dns \"{adapterName}\" all no");

                if (!string.IsNullOrEmpty(profile.DnsPrimary))
                {
                    ctx.Status = $"Primary DNS 서버 설정 중... (dns1:{profile.DnsPrimary})";
                    RunNetshCommand($"interface ipv4 set dns \"{adapterName}\" static {profile.DnsPrimary} no");
                }
                if (!string.IsNullOrEmpty(profile.DnsSecondary))
                {
                    ctx.Status = $"Secondary DNS 서버 설정 중... (dns2:{profile.DnsSecondary})";
                    RunNetshCommand($"interface ipv4 add dns \"{adapterName}\" {profile.DnsSecondary} index=2 no");
                }

                AnsiConsole.MarkupLine("[bold green]네트워크 설정 적용 완료![/]");
            });
        }

        static void RunNetshCommand(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(output))
                AnsiConsole.MarkupLine($"[grey]{output}[/]");

            if (!string.IsNullOrEmpty(error))
                AnsiConsole.MarkupLine($"[red]{error}[/]");
        }

        static List<NetworkProfile> LoadProfilesFromYaml(string fileName)
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(filePath))
            {
                AnsiConsole.MarkupLine("[red]profiles.yaml 파일을 찾을 수 없습니다![/]");
                return new List<NetworkProfile>();
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var yamlContent = File.ReadAllText(filePath);
            var yamlData = deserializer.Deserialize<YamlNetworkProfiles>(yamlContent);
            return yamlData.Profiles;
        }
    }

    public class NetworkProfile
    {
        [YamlMember(Alias = "name")] public string Name { get; set; }

        [YamlMember(Alias = "ip")] public string Ip { get; set; }

        [YamlMember(Alias = "gateway")] public string Gateway { get; set; }

        [YamlMember(Alias = "subnet")] public string SubnetMask { get; set; }

        [YamlMember(Alias = "dns1")] public string DnsPrimary { get; set; }

        [YamlMember(Alias = "dns2")] public string DnsSecondary { get; set; }
    }

    class YamlNetworkProfiles
    {
        public List<NetworkProfile> Profiles { get; set; }
    }
}