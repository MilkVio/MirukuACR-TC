using System.Numerics;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using PromeRotation.Extensions;
using PromeRotation.Helpers;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using MilkVio.Common;
using MilkVio.DPS.UniversalData;
using MilkVio.DPS.Viper.Action.Gcd;
using MilkVio.DPS.Viper.Action.OffGcd;
using MilkVio.DPS.Viper.Opener;
using MilkVio.DPS.Viper.ViperData;
using PromeRotation.Timeline;
using PromeRotation.UI;
using PromeRotation.UI.HotKey;
using PromeRotation.Updaters;
using PromeRotation.Windows;
using ActionType = PromeRotation.Data.ActionType;

namespace MilkVio.DPS.Viper;

[RotationMetadata((uint)Job.VPR, "冬十二蛇", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class ViperRotation : IRotation
{
    private readonly IRotationEventHandler _eventHandler = new ViperRotationEventHandler();
    public IRotationEventHandler GetEventHandler() => _eventHandler;

    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();
    private readonly OpenerSelector _openerSelector = new();

    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        {ViperQt.启用起手, true},
        {ViperQt.飞蛇之尾, true},
        {ViperQt.飞蛇之牙, true},
        
        {ViperQt.蛇灵气, true},
        {ViperQt.祖灵降临, true},
        {ViperQt.强碎灵蛇, true},
        
        {ViperQt.倾泻资源, false},
        {ViperQt.AOE, false},
        {ViperQt.真北, true},
        
        {ViperQt.TP身位, false},
    };
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        {"蝰蛇80级通用起手", typeof(VPR_80_G)},
        {"蝰蛇90级通用起手", typeof(VPR_90_G)},
        {"蝰蛇100级通用起手", typeof(VPR_100_G)},
        {"蝰蛇100级伊甸起手", typeof(VPR_100_FRU)}
    };

    public ViperRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 双牙术OffGcd());
        _offGcdResolvers.Add(new 蛇尾术OffGcd());
        _offGcdResolvers.Add(new 蛇灵气OffGcd());
        _offGcdResolvers.Add(new 真北OffGcd());
        
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 祖灵降临派生Gcd());
        _gcdResolvers.Add(new 祖灵降临Gcd());
        _gcdResolvers.Add(new 飞蛇之尾Gcd());
        _gcdResolvers.Add(new 强碎灵蛇派生Gcd());
        _gcdResolvers.Add(new 强碎灵蛇Gcd());
        _gcdResolvers.Add(new 基础连击Gcd());
        _gcdResolvers.Add(new 飞蛇之牙Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        
        var hotkeyPanel = new HotkeyPanel(columns: 5, title: "VPR Hotkeys");
        hotkeyPanel.AddHotkey("蛇行", new PAction(ViperSkill.蛇行, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("牵制", new PAction(MeleeUniversalSkill.牵制, ActionType.OffGcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey("真北", new PAction(MeleeUniversalSkill.真北, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("疾跑", new PAction(3, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("亲疏自行", new PAction(MeleeUniversalSkill.亲疏自行, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("内丹", new PAction(MeleeUniversalSkill.内丹, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("浴血", new PAction(MeleeUniversalSkill.浴血, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey(
            "同步镜头",
            new ToggleLogic(
                () => CameraSyncManager.CurrentMode == SyncMode.Camera,
                CameraSyncManager.ToggleCameraSync),
            iconActionId: 11404);
        hotkeyPanel.AddHotkey(
            "校准正方向",
            new ToggleLogic(
                () => CameraSyncManager.CurrentMode == SyncMode.Align,
                CameraSyncManager.ToggleAlignSync),
            customIconPath: "Resources/Align4.png");
        hotkeyPanel.AddHotkey(
            "清扫队列",
            new ExecuteLogic(() =>
            {
                ActionQueueManager.ClearAllQueues();
                ActionUpdater.Reset();
                Svc.Chat.PrintError("[PromeRotation] 清扫队列");
            }),
            customIconPath: "Resources/Clear.png");
        HotkeyManager.Instance.AddHotkeyPanel(hotkeyPanel);
    }

    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(ViperQt.启用起手) || Core.Me == null)
            return null;

        return _openerSelector.Resolve(Openers);
    }

    public PAction? NextAlways()
    {
        foreach (var resolver in _alwaysResolvers)
        {
            if (resolver.Check().Success)
                return resolver.GetAction();
        }
        return null;
    }

    public PAction? NextGcd()
    {
        foreach (var resolver in _gcdResolvers)
        {
            if (resolver.Check().Success)
            {
                return resolver.GetAction();
            }
        }
        return null;
    }

    public PAction? NextOffGcd()
    {
        foreach (var resolver in _offGcdResolvers)
        {
            if (resolver.Check().Success)
            {
                return resolver.GetAction();
            }
        }
        return null;
    }

    public void UpdateDebugStatus()
    {
        RotationManager.AlwaysSolverStatus.Clear();
        RotationManager.GcdSolverStatus.Clear();
        RotationManager.OffGcdSolverStatus.Clear();

        foreach (var resolver in _alwaysResolvers)
        {
            var result = resolver.Check();
            RotationManager.AlwaysSolverStatus.Add(new SolverStatus
            {
                Name = resolver.GetType().Name,
                Success = result.Success,
                Message = result.Message
            });
        }

        foreach (var resolver in _gcdResolvers)
        {
            var result = resolver.Check();
            RotationManager.GcdSolverStatus.Add(new SolverStatus
            {
                Name = resolver.GetType().Name,
                Success = result.Success,
                Message = result.Message
            });
        }

        foreach (var resolver in _offGcdResolvers)
        {
            var result = resolver.Check();
            RotationManager.OffGcdSolverStatus.Add(new SolverStatus
            {
                Name = resolver.GetType().Name,
                Success = result.Success,
                Message = result.Message
            });
        }
    }

    public void DrawQTs()
    {

    }

    public void DrawSettings()
    {
        if (ImGui.BeginTabBar("Settings"))
        {
            if (ImGui.BeginTabItem("设置"))
            {
                DrawGeneral();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("开发用"))
            {
                DrawDev();
                ImGui.EndTabItem();
            }
        }
    }

    private void DrawGeneral()
    {
        _openerSelector.DrawCombo("起手选择", Openers);
    }

    private static readonly Vector4 GreenText = new(0f, 1f, 0f, 1f);
    private static readonly Vector4 RedText = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 YellowText = new(1f, 1f, 0f, 1f);

    private void DrawDev()
    {
        ImGui.Text($"ComboLeft: {ActionHelper.GetComboLeftTime()}");
        ImGui.Text($"飞蛇之魂层数: {JobGaugeHelper.VPR.飞蛇之魂层数}");
        ImGui.Text($"灵力值: {JobGaugeHelper.VPR.灵力值}");
        ImGui.Text($"祖灵力档数: {JobGaugeHelper.VPR.祖灵力档数}");
        ImGui.Text($"蛇剑连状态: {JobGaugeHelper.VPR.蛇剑连状态}");
        ImGui.Text($"蛇尾术状态: {JobGaugeHelper.VPR.蛇尾术状态}");
        ImGui.Text("--------");
        DrawSkillHighlight("咬噬尖齿左1", ViperSkill.咬噬尖齿左1);
        DrawSkillHighlight("穿裂尖齿右1", ViperSkill.穿裂尖齿右1);
        DrawSkillHighlight("猛袭利齿左2", ViperSkill.猛袭利齿左2);
        DrawSkillHighlight("急速利齿右2", ViperSkill.急速利齿右2);
        DrawSkillHighlightGreen("侧击獠齿左3绿", ViperSkill.侧击獠齿左3绿);
        DrawSkillHighlightGreen("侧裂獠齿右3绿", ViperSkill.侧裂獠齿右3绿);
        DrawSkillHighlightRed("侧击獠齿左3红", ViperSkill.侧击獠齿左3红);
        DrawSkillHighlightRed("侧裂獠齿右3红", ViperSkill.侧裂獠齿右3红);
    }

    private static void DrawSkillHighlight(string name, uint skillId)
    {
        var highlighted = skillId.IsActionHighlighted();
        ImGui.Text($"{name}: ");
        ImGui.SameLine();
        ImGui.TextColored(highlighted ? YellowText : RedText, highlighted.ToString());
    }

    private static void DrawSkillHighlightGreen(string name, uint skillId)
    {
        var highlighted = skillId.IsActionHighlighted();
        ImGui.TextColored(GreenText, $"{name}: ");
        ImGui.SameLine();
        ImGui.TextColored(highlighted ? YellowText : RedText, highlighted.ToString());
    }

    private static void DrawSkillHighlightRed(string name, uint skillId)
    {
        var highlighted = skillId.IsActionHighlighted();
        ImGui.TextColored(RedText, $"{name}: ");
        ImGui.SameLine();
        ImGui.TextColored(highlighted ? YellowText : RedText, highlighted.ToString());
    }
}
