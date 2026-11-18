using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NabyScape;

internal static class PremiumExperienceTests
{
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(send(request));}
    internal static void Run(Action<string,Action> test)
    {
        void Assert(bool value){if(!value)throw new Exception("Premium experience fixture failed.");}
        test("Premium preferences normalize nonfinite values and unknown modes",()=>
        {
            var s=new Settings{AmbientStrength=double.NaN,AmbientSpread=20,AmbientMotion="unknown",InteractionMotion="unknown",NotificationSeconds=double.PositiveInfinity,NotificationAnimation="unknown",QuietStart=-1,QuietEnd=30};s.Normalize();
            Assert(s.AmbientStrength==.5 && s.AmbientSpread==2.5 && s.AmbientMotion=="Drift" && s.InteractionMotion=="Subtle" && s.NotificationSeconds==12 && s.NotificationAnimation=="Lift" && s.QuietStart==0 && s.QuietEnd==23);
        });
        test("Quiet hours cover overnight boundaries without silencing the daytime",()=>
        {
            DateTimeOffset At(int hour)=>new(new DateTime(2026,10,21,hour,0,0),TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026,10,21,hour,0,0)));
            var s=new Settings{QuietHours=true,QuietStart=22,QuietEnd=7};Assert(NotificationPolicy.Quiet(s,At(22)) && NotificationPolicy.Quiet(s,At(0)) && NotificationPolicy.Quiet(s,At(6)) && !NotificationPolicy.Quiet(s,At(7)) && !NotificationPolicy.Quiet(s,At(21)));
            s.QuietStart=9;s.QuietEnd=17;Assert(NotificationPolicy.Quiet(s,At(9)) && !NotificationPolicy.Quiet(s,At(17)) && !NotificationPolicy.Quiet(s,At(22)));s.QuietStart=s.QuietEnd;Assert(!NotificationPolicy.Quiet(s,At(17)));
        });
        test("Snooze suppresses both routes and resumes without changing delivery preferences",()=>
        {
            var now=DateTimeOffset.UtcNow;var s=new Settings{NotificationDelivery="Both",NotificationsSnoozedUntil=now.AddMinutes(15)};var manager=new InternalNotifications();int windows=0;
            manager.Post(s,new("timer","Done","Fixture"),false,_=>windows++,now);Assert(manager.Count==0 && windows==0);
            manager.Post(s,new("timer","Done","Fixture"),false,_=>windows++,now.AddMinutes(16));Assert(manager.Count==1 && windows==1 && s.NotificationDelivery=="Both");
        });
        test("Notification lifetime is configurable and keeps busy details available",()=>
        {
            var now=DateTimeOffset.UtcNow;var manager=new InternalNotifications();manager.Post(new Settings(),new("timer","Done","Fixture"),false,_=>{},now);
            manager.Tick(now.AddSeconds(5),false,20);Assert(manager.Count==1);manager.Tick(now.AddSeconds(21),true,20);Assert(manager.Count==1);manager.Tick(now.AddSeconds(40),false,20);Assert(manager.Count==1);manager.Tick(now.AddSeconds(42),false,20);Assert(manager.Count==0);
        });
        test("Layout exports include ambient appearance and exclude snooze and quiet schedules",()=>
        {
            var source=new Settings{AmbientStrength=.82,AmbientMotion="Breathe",InteractionMotion="Expressive",NotificationsSnoozedUntil=DateTimeOffset.UtcNow.AddHours(1),QuietHours=true};var target=new Settings();string json=CustomizationProfile.Export(source);CustomizationProfile.Import(json,target);
            Assert(target.AmbientStrength==.82 && target.AmbientMotion=="Breathe" && target.InteractionMotion=="Expressive" && !json.Contains("NotificationsSnoozedUntil") && !json.Contains("QuietHours"));
        });
    }
}
