using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.JobGauge.Enums;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using PromeRotation.Data;
using PromeRotation.Extensions;
using PromeRotation.Helpers;
using PromeRotation.Managers;
using PromeRotation.Resolvers;

using MilkVio.Tank.DRK.Action.Gcd;
using MilkVio.Tank.DRK.Action.OffGcd;
using MilkVio.Tank.DRK.DRKData;
using MilkVio.Tank.DRK.Opener;
using PromeRotation.Timeline;
using PromeRotation.Timeline.Core;
using PromeRotation.UI;
using PromeRotation.UI.HotKey;
using PromeRotation.Windows;
using MilkVio.Common;

namespace MilkVio.Tank.DRK;

[RotationMetadata((uint)Job.DRK, "暗黑骑士创世版", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class DrakKnightRotation : IRotation, IRotationMeta
{
    public static IJobNodeProvider? NodeProvider { get; } = new DarkKnightJobNodeProvider();
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler eventHandler = new DarkKnightRotationEventHandler();
    public IRotationEventHandler GetEventHandler() => eventHandler;
    
    // 管理该职业所有的决策解析器
    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();
    private readonly OpenerSelector _openerSelector = new();
    
    
    /* todo
     * 最紧急：增加暗血防溢出 在血乱增加暗血检测 > 70直接不按 写一个优先级最高的 
     * Done 1.需要增加暗影峰自动控制 例如弗雷冷却=0或者冷却剩余20秒的时候将蓝量阈值提升到9000 2回蓝600 自然跳蓝200？
     * 2.增加各技能QT控制？
     * 3.区分倾斜资源和最终爆发：
     * 倾泻资源：这个倾泻资源 对于每个职业有不同的解释 例如我认为暗黑骑士的倾泻资源应该包含：60秒的所有技能 所有蓝量 马桶 暗血 >= 50就打一个暗技
     * 最终爆发：包含倾泻资源的基础上 将120的所有东西按照伤害最高的优先级都打出去
     * 4.似乎还没有检测是否有免费的暗影峰
     * 5.70级尼玛怎么检测爆发啊，又没有120
     * 
     */
    
    // ACR的数据
    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        { DRKQt.启用起手, true },
        { DRKQt.不打60, false },
        { DRKQt.不打120, false },
        { DRKQt.保留3000蓝, true },
        { DRKQt.倾泻资源, false },
        { DRKQt.延后掠影的蔑视, true },
        { DRKQt.马桶对齐120, false },
        { DRKQt.伤残, true },
        { DRKQt.最终爆发, false },
    };
    
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        { "暗黑骑士 绝亚DollSkip起手", typeof(DRK_80_TEADS_ST) },
        { "暗黑骑士 绝伊甸MT专用起手", typeof(DRK_100_FRU_MT) },
        { "暗黑骑士 绝伊甸MT专用起手2", typeof(DRK_100_FRU_MT_FA) },
        { "暗黑骑士 绝欧米茄MT起手", typeof(DRK_90_TOP_MT) },
        { "暗黑骑士 绝欧米茄ST起手", typeof(DRK_90_TOP_ST) },
    };
    
    public DrakKnightRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 暗影锋爆发防溢出OffGcd());
        _offGcdResolvers.Add(new 弗雷OffGcd());
        _offGcdResolvers.Add(new 血乱OffGcd());
        _offGcdResolvers.Add(new 精雕吸血OffGcd());
        _offGcdResolvers.Add(new 腐秽大地OffGcd());
        _offGcdResolvers.Add(new 腐秽黑暗OffGcd());
        _offGcdResolvers.Add(new 暗影锋OffGcd());
        _offGcdResolvers.Add(new 暗影使者OffGcd());
        
        
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 掠影的蔑视Gcd());
        _gcdResolvers.Add(new 暗血技Gcd());
        _gcdResolvers.Add(new 伤残Gcd());
        _gcdResolvers.Add(new BaseGcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        
        var hotkeyPanel = new HotkeyPanel(columns: 5, title: "DRK Hotkeys");
        hotkeyPanel.AddHotkey("暗影步", new PAction(DRKSkill.暗影步, ActionType.OffGcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey("挑衅", new PAction(DRKSkill.挑衅, ActionType.OffGcd, ActionTargetType.Target));
        hotkeyPanel.AddHotkey(
            "退避ST",
            new ExecuteLogic(() =>
                ActionQueueManager.Enqueue(
                    new PAction(DRKSkill.退避, ActionType.OffGcd, ActionTargetType.PartyMember2), true)),
            iconActionId: DRKSkill.退避,
            customIconPath: "Resources/DRK/TBST.png");
        hotkeyPanel.AddHotkey("至黑之夜", new PAction(DRKSkill.至黑之夜, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("弃明投暗", new PAction(DRKSkill.弃明投暗, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("铁壁", new PAction(DRKSkill.铁壁, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("暗影卫", new PAction(DRKSkill.暗影卫, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("献奉", new PAction(DRKSkill.献奉, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("亲疏自行", new PAction(DRKSkill.亲疏自行, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("血仇", new PAction(DRKSkill.血仇, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("暗黑布道", new PAction(DRKSkill.暗黑布道, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey(
            "黑盾ST",
            new ExecuteLogic(() =>
                ActionQueueManager.Enqueue(
                    new PAction(DRKSkill.至黑之夜, ActionType.OffGcd, ActionTargetType.PartyMember2), true)),
            iconActionId: DRKSkill.至黑之夜,
            customIconPath: "Resources/DRK/HDST.png");
        hotkeyPanel.AddHotkey(
            "奉献ST",
            new ExecuteLogic(() =>
                ActionQueueManager.Enqueue(
                    new PAction(DRKSkill.献奉, ActionType.OffGcd, ActionTargetType.PartyMember2), true)),
            iconActionId: DRKSkill.献奉,
            customIconPath: "Resources/DRK/XFST.png");
        hotkeyPanel.AddHotkey("深恶痛绝", new PAction(DRKSkill.深恶痛绝, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("解除深恶痛绝", new PAction(DRKSkill.解除深恶痛绝, ActionType.OffGcd, ActionTargetType.Self));
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
                Svc.Chat.PrintError("[PromeRotation] 清扫队列");
            }),
            customIconPath: "Resources/Clear.png");
        HotkeyManager.Instance.AddHotkeyPanel(hotkeyPanel);
        
        
        // todo
        // 增加留资源 不打暗血 之类的
    }
    
    // 该职业的起手
    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(DRKQt.启用起手) || Core.Me == null)
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
        ImGui.Text($"暗影使者充能情况：{DRKSkill.暗影使者.GetActionCharges()}");
        ImGui.Text($"暗黑剩余时间：{JobGaugeHelper.DRK.DarksideTimeRemaining.ToString()}");
        ImGui.Text($"弗雷剩余时间：{JobGaugeHelper.DRK.ShadowTimeRemaining.ToString()}");
        ImGui.Text($"测试：{DRKSkill.至黑之夜.GetActionCharges().ToString()}");
        
        var iconTexture = DRKSkill.伤残.GetActionIcon();
        //var jocbic = IconHelper.GetActionIcon(62132);
    }
    
}
