using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using PromeRotation.Helpers;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.DPS.Samurai.Action.Gcd;
using MilkVio.DPS.Samurai.Action.OffGcd;
using MilkVio.DPS.Samurai.Opener;
using MilkVio.DPS.Samurai.SAMData;
using MilkVio.DPS.UniversalData;
using PromeRotation.Timeline;
using PromeRotation.UI;
using PromeRotation.UI.HotKey;
using PromeRotation.Updaters;
using PromeRotation.Windows;
using AOEGcd = MilkVio.DPS.Ninja.Action.Gcd.AOEGcd;
using MilkVio.Common;

namespace MilkVio.DPS.Samurai;

[RotationMetadata((uint)Job.SAM, "团队型武士", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class SamuraiRotation : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new SamuraiRotationEventHandler();
    public IRotationEventHandler GetEventHandler() => _eventHandler;
    
    // 管理该职业所有的决策解析器
    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();
    private readonly OpenerSelector _openerSelector = new();
    
    // 实现对外暴露的静态属性
    // Qt列表
    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        {SAMData.SAMQt.启用起手, true},
        {SAMData.SAMQt.倾泻资源, false},
        {SAMData.SAMQt.AOE, false},
        {SAMData.SAMQt.不打120, false},
        {SAMData.SAMQt.彼岸花, true},
        {SAMData.SAMQt.明镜止水, true},
        {SAMData.SAMQt.燕飞, true},
        {SAMData.SAMQt.真北, true},
        {SAMData.SAMQt.TP身位, false},
        {SAMData.SAMQt.立即回返, true},
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        {"武士绝亚DollSkip起手", typeof(SAM_80_TEADS)},
        {"武士绝欧起手", typeof(SAM_90_OMG)},
    };
    
    public SamuraiRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 残心OffGcd());
        _offGcdResolvers.Add(new 照破OffGcd());
        _offGcdResolvers.Add(new 必杀剑_红莲OffGcd());
        _offGcdResolvers.Add(new 必杀剑_闪影OffGcd());
        _offGcdResolvers.Add(new 意气冲天OffGcd());
        _offGcdResolvers.Add(new 明镜止水OffGcd());
        _offGcdResolvers.Add(new 必杀剑_九天OffGcd());
        _offGcdResolvers.Add(new 必杀剑_震天OffGcd());
        _offGcdResolvers.Add(new 真北OffGcd());
        
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 奥义斩浪Gcd());
        _gcdResolvers.Add(new 燕回返Gcd());
        _gcdResolvers.Add(new 居合术Gcd());
        _gcdResolvers.Add(new 明镜Gcd());
        _gcdResolvers.Add(new AOEGcd());
        _gcdResolvers.Add(new 雪月花闪连Gcd());
        _gcdResolvers.Add(new 连击1Gcd());
        _gcdResolvers.Add(new 燕飞Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        
        var hotkeyPanel = new HotkeyPanel(columns: 5, title: "SAM Hotkeys");
        hotkeyPanel.AddHotkey("牵制", new PAction(MeleeUniversalSkill.牵制, ActionType.OffGcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey("真北", new PAction(MeleeUniversalSkill.真北, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("心眼", new PAction(SAMSkill.心眼, ActionType.OffGcd, ActionTargetType.Self));
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
    
    // 该职业的起手
    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(SAMData.SAMQt.启用起手) || Core.Me == null)
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
        // 遍历所有GCD解析器
        foreach (var resolver in _gcdResolvers)
        {
            if (resolver.Check().Success)
            {
                // 找到第一个满足条件的，返回它的决策结果
                return resolver.GetAction();
            }
        }
        // 如果所有求解器都不满足条件，返回null
        return null;
    }
    
    public PAction? NextOffGcd()
    {
        // 遍历所有oGCD解析器 同上
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
        // 清空上一帧的旧数据
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
        
        // GCD状态列表
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
        
        // OGCD状态列表
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
    
    private void DrawDev()
    {
        ImGui.Text($"剑气：{JobGaugeHelper.SAM.剑气}");
        ImGui.Text($"剑压：{JobGaugeHelper.SAM.剑压}");
        ImGui.Text($"雪：{JobGaugeHelper.SAM.HasYuki}");
        ImGui.Text($"月：{JobGaugeHelper.SAM.HasMoon}");
        ImGui.Text($"花：{JobGaugeHelper.SAM.HasHana}");
        ImGui.Text($"回返斩浪可用：{SamuraiHelper.回返斩浪可用()}");
        ImGui.Text($"当前可用居合：{SamuraiHelper.GetCurrent居合类型().ToString()}");
        ImGui.Text($"当前求解最佳技能：{SamuraiHelper.GetBestComboType().ToString()}");
        ImGui.Text($"明镜层数：{SamuraiHelper.明镜止水层数()}");
        ImGui.Text($"当前需要的身位：{SamuraiHelper.GetNeedPositional()}");
        
        ImGui.Text($"必杀剑_红莲:{ActionHelper.IsActionAvailableByLevelAndQuest(SAMSkill.必杀剑_红莲).ToString()}");
        ImGui.Text($"照破:{ActionHelper.IsActionAvailableByLevelAndQuest(SAMSkill.照破).ToString()}");
        ImGui.Text($"残心:{ActionHelper.IsActionAvailableByLevelAndQuest(SAMSkill.残心).ToString()}");
    }
    
}
