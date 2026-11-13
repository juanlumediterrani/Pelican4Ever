using Pelican4Ever.Core.Admin;
using System.Text.Json;

// Localiza Pelican4Ever/assets/dashboard.html subiendo desde la carpeta de ejecución.
string templatePath = null;
string current = AppContext.BaseDirectory;
while (!string.IsNullOrEmpty(current) && templatePath == null)
{
    string candidate = Path.Combine(current, "Pelican4Ever", "assets", "dashboard.html");
    if (File.Exists(candidate))
        templatePath = candidate;
    else
        current = Directory.GetParent(current)?.FullName;
}

if (templatePath == null)
    throw new FileNotFoundException("No se encontró Pelican4Ever/assets/dashboard.html.");

string template = File.ReadAllText(templatePath);
string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "p4e-web-panel-review");
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "dashboard.html"), AdminApiLogic.RenderDashboardHtml(template));
File.WriteAllText(Path.Combine(output, "fixtures.json"), JsonSerializer.Serialize(new {
    status = new ServerStatusDto { OnlinePlayersCount=2, RegisteredPlayersCount=3, OccupiedDoorsCount=1, TotalDoorsCount=16, ActiveMarketListingsCount=4 },
    clock = new HostClockDto {Year=1, Season="fall", DayOfMonth=6, GameTimeOfDay=1430, TotalGameDays=61, IsMultiplayerServer=true, SaveFolderName="PanelTest", SecondsUntilNextRollover=900, NextRolloverLocalTime="02:00"},
    players = new HostPlayersResponseDto {TotalOnline=2,TotalRegistered=3,Players = new() {new HostPlayerDetailDto {PlayerId=123,Name="<img src=x onerror=window.xss=1>",IsOnline=true,SpaBuffsUsed=2,LocationName="Town",SleepState="Rested"}}},
    festival = new HostFestivalDto {IsFestivalToday=true,FestivalName="Festival de prueba",WindowStart=1800,WindowEnd=2350,IsWindowOpen=true},
    events = new HostEventsResponseDto {LastEventId=1, Events=new() {new HostEventEntry {Id=1, TimestampUtc="2026-11-06T14:30:00Z",Summary="Evento de prueba"}}},
    map = new HostMapDto {LocationName="Town",Width=4,Height=4,Tiles = new byte[] {1,3,8,11, 6,7,2,4, 1,1,3,1, 1,1,1,1},AvailableMaps=new() {"Town","Farm","Cabin\" onclick=\"window.xss=1"},Players=new() {new MapPlayerMarker {PlayerId=123,Name="Host",TileX=3,TileY=0,IsHost=true}}}
}));
