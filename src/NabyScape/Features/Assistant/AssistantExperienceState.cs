using System;
namespace NabyScape;
internal sealed partial class AssistantRuntime
{
    internal bool SpeechCheckBusy {get;private set;}
    private Action? cancelSpeechCheck;
    private bool resumeWakeAfterCheck;
    private int speechCheckWakeVersion;
    internal void BeginSpeechCheck(Action cancel){if(SpeechCheckBusy || Speech.Busy || VoiceConnected || Busy)throw new InvalidOperationException("Finish the current voice operation first.");bool resume=WakeArmed;StopSession();resumeWakeAfterCheck=resume;speechCheckWakeVersion=wakeVersion;SpeechCheckBusy=true;Speech.Testing=true;cancelSpeechCheck=cancel;}
    internal void EndSpeechCheck(){SpeechCheckBusy=false;Speech.Testing=false;cancelSpeechCheck=null;if(resumeWakeAfterCheck && speechCheckWakeVersion==wakeVersion && Options.Enabled && Options.WakeWord.Enabled){wakeWanted=true;wakeNotBefore=Environment.TickCount64+1200;WakeStatus="Speech check ended · wake word will resume.";}resumeWakeAfterCheck=false;Notify();}
    internal string CaptureSummary=>Localization.Text(WakeListening?"Wake listening":WakeArmed?"Wake preparing":Options.WakeWord.Enabled?"Wake paused":"Wake off")+" · "+Localization.Text(SpeechCheckBusy?"Speech test active":VoiceConnected?"Mic on":"Mic off")+" · "+(Desktop.Sharing?Localization.Format("Sharing {0}",Desktop.SourceName):Localization.Text("Screen not shared"));
    internal string SetupSummary
    {
        get
        {
            string listening=Localization.Text("Realtime provider"),speaking=listening;var local=Options.LocalVoice;
            if(local.Enabled)
            {
                if(local.Managed){try{listening=AssistantOwnSpeech.Asr(local).Name;speaking=AssistantOwnSpeech.Tts(local).Name;}catch{listening=Localization.Text("Model files need attention");speaking=Localization.Text("Open Voice setup");}}
                else{listening=local.AsrModel;speaking=local.TtsEngine;}
            }
            return Localization.Format("AI replies: {0}\nListening: {1}\nSpeaking: {2}\n{3}",Provider?.Name??Localization.Text("Not configured"),listening,speaking,CaptureSummary);
        }
    }
}
