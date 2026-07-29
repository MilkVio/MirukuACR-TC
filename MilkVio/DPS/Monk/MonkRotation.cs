using System;
using System.Collections.Generic;
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
using MilkVio.DPS.Monk.Action.Gcd;
using MilkVio.DPS.Monk.Action.OffGcd;
using MilkVio.DPS.Monk.MNKData;
using MilkVio.DPS.Monk.Opener;
using MilkVio.DPS.UniversalData;
using PromeRotation.Timeline;
using PromeRotation.UI;
using PromeRotation.UI.HotKey;
using PromeRotation.Updaters;

namespace MilkVio.DPS.Monk;

[RotationMetadata((uint)Job.MNK, "Prome武僧", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class MonkRotation : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new MonkRotationEventHandler();
    public IRotationEventHandler GetEventHandler() => _eventHandler;
    
    // 管理该职业所有的决策解析器
    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();
    private readonly OpenerSelector _openerSelector = new();
    
    // 实现对外暴露的数据
    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        { MNKQt.启用起手, true },
        { MNKQt.不打60, false },
        { MNKQt.不打120, false },
        { MNKQt.AOE, false },
        { MNKQt.强制对齐爆发, false },
        { MNKQt.延后绝空拳, false },
        { MNKQt.疾风对齐120, false },
        { MNKQt.倾泻资源, false },
        { MNKQt.最终爆发, false },
        { MNKQt.攒资源, false },
        { MNKQt.自动演武, true },
        { MNKQt.搓豆子, true },
        { MNKQt.必杀技, true },
        { MNKQt.震脚打阴, false },
        { MNKQt.震脚打阳, false },
        { MNKQt.无目标搓必杀技, false },
        { MNKQt.真北, true },
        { MNKQt.TP身位, false },
        { MNKQt.不打震脚, false },
    };
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        { "武僧神兵起手", typeof(MNK_UWU_U) },
        { "武僧70通用3G爆发起手", typeof(MNK_70_U) },
        { "武僧绝亚DollSkip起手", typeof(MNK_80_TEADS) },
        { "武僧绝欧起手", typeof(MNK_Omega_U) },
        { "武僧100通用3G爆发起手", typeof(MNK_100_U) },
    };
    
    public MonkRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 义结金兰OffGcd());
        _offGcdResolvers.Add(new 红莲OffGcd());
        _offGcdResolvers.Add(new 疾风OffGcd());
        _offGcdResolvers.Add(new 震脚OffGcd());
        _offGcdResolvers.Add(new 阴阳斗气斩OffGcd());
        _offGcdResolvers.Add(new 真北OffGcd());
        
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 斗气弹Gcd());
        _gcdResolvers.Add(new 必杀技Gcd());
        _gcdResolvers.Add(new 绝空拳Gcd());
        _gcdResolvers.Add(new 震脚AOEGcd());
        _gcdResolvers.Add(new 基础AOEGcd());
        _gcdResolvers.Add(new 震脚Gcd());
        _gcdResolvers.Add(new 基础Gcd());
        _gcdResolvers.Add(new 搓豆子Gcd());
        _gcdResolvers.Add(new 演武Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        
        var hotkeyPanel = new HotkeyPanel(columns: 5, title: "MNK Hotkeys");
        hotkeyPanel.AddHotkey("六合星导脚", new PAction(MNKSkill.六合星导脚, ActionType.Gcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey("牵制", new PAction(MeleeUniversalSkill.牵制, ActionType.OffGcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey("真言", new PAction(MNKSkill.真言, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("浴血", new PAction(MeleeUniversalSkill.浴血, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("内丹", new PAction(MeleeUniversalSkill.内丹, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("亲疏自行", new PAction(MeleeUniversalSkill.亲疏自行, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("演武", new PAction(MNKSkill.演武, ActionType.Gcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("疾跑", new PAction(3, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey(
            "SSSLB",
            new ExecuteLogic(() =>
            {
                List<PAction> sssLb =
                [
                    new PAction(MNKSkill.六合星导脚, ActionType.Gcd, ActionTargetType.Target)
                    {
                        RequiresVerification = true
                    },
                    new PAction(
                        LimitBreakHelper.GetLimitBreakActionId(),
                        ActionType.OffGcd,
                        ActionTargetType.Target)
                ];
                ActionQueueManager.Enqueue(sssLb, isHighPriority: true);
            }),
            iconActionId: 202);
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
        if (!PromeSettings.Instance.GetQt(MNKQt.启用起手) || Core.Me == null)
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
        ImGui.Text($"交战时间：{EngageManager.GetBattleTime().ToString()}");
        ImGui.Text($"是否在移动：{MoveManager.IsLocalPlayerMoving.ToString()}");
        ImGui.Text($"5m内的敌人：{TargetHelper.EnemyIn5m()}");
        ImGui.Text($"震脚剩余层数：{MonkHelper.GetRealPbCharge()}");
        ImGui.Text($"义结金兰剩余时间：{Core.Me.GetStatusLeftTime(MNKBuff.义结金兰)}");
        ImGui.Text($"红莲剩余时间：{Core.Me.GetStatusLeftTime(MNKBuff.红莲极意)}");
        ImGui.Text($"演武状态：{Core.Me.HasStatus(MNKSkill.演武)}");
        ImGui.Text($"猴1：{JobGaugeHelper.MNK.OpoOpoFury.ToString()}");
        ImGui.SameLine();
        ImGui.Text($"龙2：{JobGaugeHelper.MNK.RaptorFury.ToString()}");
        ImGui.SameLine();
        ImGui.Text($"豹3：{JobGaugeHelper.MNK.CoeurlFury.ToString()}");
        ImGui.Text($"脉轮：{JobGaugeHelper.MNK.Chakra.ToString()}");
        ImGui.Text($"三个必杀技豆子：{JobGaugeHelper.MNK.BeastChakra[0].ToString()} {JobGaugeHelper.MNK.BeastChakra[1].ToString()} {JobGaugeHelper.MNK.BeastChakra[2].ToString()}");
        ImGui.Text($"必杀技剩余时间：{JobGaugeHelper.MNK.BlitzTimeRemaining.ToString()}");
        ImGui.Text($"必杀技量谱：{JobGaugeHelper.MNK.Nadi}");
        ImGui.Text($"当前身形：{MonkHelper.GetCurrentBeastGroup().ToString()}");
    }
    
}
