using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.JobGauge.Enums;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Core;
using PromeRotation.Data;
using PromeRotation.Extensions;
using PromeRotation.Helpers;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.Common;
using MilkVio.DPS.Pictomancer.Action.Gcd;
using MilkVio.DPS.Pictomancer.Action.OffGcd;
using MilkVio.DPS.Pictomancer.Opener;
using MilkVio.DPS.Pictomancer.PCTData;
using MilkVio.DPS.UniversalData;
using PromeRotation.Timeline;
using PromeRotation.UI;
using PromeRotation.UI.HotKey;
using PromeRotation.Windows;
using 锤子Gcd = MilkVio.DPS.Pictomancer.Action.Gcd.锤子Gcd;

namespace MilkVio.DPS.Pictomancer;

[RotationMetadata((uint)Job.PCT, "画家元始版", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class PictomancerRotation : IRotation
{
    // 创建一个属于该职业的回调
    private readonly IRotationEventHandler _eventHandler = new PictomancerRotationEventHandler();
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
        {PCTQt.启用起手, true},
        {PCTQt.上天画画, false},
        {PCTQt.AOE, true},
        
        {PCTQt.倾泻资源, false},
        {PCTQt.倾泻彩绘, false},
        {PCTQt.倾泻减色, false},
        
        {PCTQt.动物画, true},
        {PCTQt.武器画, true},
        {PCTQt.风景画, true},
        
        {PCTQt.动物构想, true},
        {PCTQt.重锤构想, true},
        {PCTQt.星空构想, true},
        
        {PCTQt.立即动物画, false},
        {PCTQt.立即武器画, false},
        {PCTQt.立即风景画, false},
        
        {PCTQt.动物炮, true},
        {PCTQt.快打锤子, false},
        {PCTQt.不打减色, false},
        
        {PCTQt.即刻风景画, false},
    };
    // 起手列表
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        {"绘灵法师绝亚DollSkip起手", typeof(PCT_80_TEADS)},
        {"绘灵法师绝伊甸专用起手", typeof(PCT_100_FRU)},
        {"绘灵法师70级2G团辅起手", typeof(PCT_70_2G_U)},
        {"绘灵法师90级2G团辅起手", typeof(PCT_90_2G_U)},
        {"绘灵法师100级2G团辅起手", typeof(PCT_100_2G_U)},
        {"绘灵法师100级3G团辅起手", typeof(PCT_100_3G_U)}
    };
    
    public PictomancerRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        _offGcdResolvers.Add(new 星空构想OffGcd());
        _offGcdResolvers.Add(new 减色混合OffGcd());
        _offGcdResolvers.Add(new 即刻风景画OffGcd());
        _offGcdResolvers.Add(new 生物炮OffGcd());
        _offGcdResolvers.Add(new 重锤构想OffGcd());
        _offGcdResolvers.Add(new 动物构想OffGcd());
        _offGcdResolvers.Add(new 醒梦OffGcd());
        
        // 爆发状态下的GCD
        _gcdResolvers.Add(new 风景彩绘_立即Gcd());
        _gcdResolvers.Add(new 武器彩绘_立即Gcd());
        _gcdResolvers.Add(new 动物彩绘_立即Gcd());
        _gcdResolvers.Add(new 锤子防过期Gcd());
        _gcdResolvers.Add(new 反转_加速Gcd());
        _gcdResolvers.Add(new 黑豆子_加速Gcd());
        _gcdResolvers.Add(new 天星棱光Gcd());
        _gcdResolvers.Add(new 彩虹点滴Gcd());
        _gcdResolvers.Add(new 黑豆子_普通Gcd());
        _gcdResolvers.Add(new 锤子Gcd());
        _gcdResolvers.Add(new 基础反转AOEGcd());
        _gcdResolvers.Add(new 基础反转Gcd());
        _gcdResolvers.Add(new 武器彩绘Gcd());
        _gcdResolvers.Add(new 生物彩绘Gcd());
        _gcdResolvers.Add(new 风景彩绘Gcd());
        _gcdResolvers.Add(new 基础AOEGcd());
        _gcdResolvers.Add(new 基础Gcd());
        _gcdResolvers.Add(new 白豆子Gcd());
        
        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
        
        
        var hotkeyPanel = new HotkeyPanel(columns: 5, title: "PCT Hotkeys");
        hotkeyPanel.AddHotkey("沉稳咏唱", new PAction(MageUniversalSkill.沉稳咏唱, ActionType.OffGcd, ActionTargetType.Self));
        hotkeyPanel.AddHotkey("混乱", new PAction(MageUniversalSkill.混乱, ActionType.OffGcd, ActionTargetType.Target));
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
    }
    
    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(PCTQt.启用起手) || Core.Me == null)
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
            if (ImGui.BeginTabItem("开发用2"))
            {
                DrawDev2();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("开发用3"))
            {
                DrawDev3();
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
        ImGui.Text($"Paint：{JobGaugeHelper.PCT.Paint}");
        ImGui.Text($"PalleteGauge：{JobGaugeHelper.PCT.PalleteGauge}");
        ImGui.Text($"CreatureMotifDrawn：{JobGaugeHelper.PCT.CreatureMotifDrawn}");
        ImGui.Text($"WeaponMotifDrawn：{JobGaugeHelper.PCT.WeaponMotifDrawn}");
        ImGui.Text($"LandscapeMotifDrawn：{JobGaugeHelper.PCT.LandscapeMotifDrawn}");
        ImGui.Text($"MooglePortraitReady：{JobGaugeHelper.PCT.MooglePortraitReady}");
        ImGui.Text($"MadeenPortraitReady：{JobGaugeHelper.PCT.MadeenPortraitReady}");
        var canvasFlags = JobGaugeHelper.PCT.CanvasFlags;
        var creatureFlags = JobGaugeHelper.PCT.CreatureFlags;
        
        ImGui.Text($"CanvasFlags：");
        foreach (CanvasFlags flag in Enum.GetValues(typeof(CanvasFlags)))
        {
            if (canvasFlags.HasFlag(flag) && flag != 0)
            {
                ImGui.SameLine();
                ImGui.Text(flag.ToString());
            }
        }
        ImGui.Text($"CreatureFlags：");
        foreach (CreatureFlags flag in Enum.GetValues(typeof(CreatureFlags)))
        {
            if (creatureFlags.HasFlag(flag) && flag != 0)
            {
                ImGui.SameLine();
                ImGui.Text(flag.ToString());
            }
        }
    }

    private void DrawDev2()
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        ImGui.Text("彩绘技能变化：");
        ImGui.Text($"动物彩绘变化:34689 → {ActionHelper.GetAdjustedActionId(PCTSkill.动物彩绘)}({sheet.GetRowOrDefault(ActionHelper.GetAdjustedActionId(PCTSkill.动物彩绘)).GetActionName()})");
        ImGui.Text($"武器彩绘变化:34690 → {ActionHelper.GetAdjustedActionId(PCTSkill.武器彩绘)}({sheet.GetRowOrDefault(ActionHelper.GetAdjustedActionId(PCTSkill.武器彩绘)).GetActionName()})");
        ImGui.Text($"风景彩绘变化:34691 → {ActionHelper.GetAdjustedActionId(PCTSkill.风景彩绘)}({sheet.GetRowOrDefault(ActionHelper.GetAdjustedActionId(PCTSkill.风景彩绘)).GetActionName()})");
        
        ImGui.Text("构想技能变化：");
        ImGui.Text($"动物构想变化:35347 → {ActionHelper.GetAdjustedActionId(PCTSkill.动物构想)}({sheet.GetRowOrDefault(ActionHelper.GetAdjustedActionId(PCTSkill.动物构想)).GetActionName()})");
        ImGui.Text($"武器构想变化:35348 → {ActionHelper.GetAdjustedActionId(PCTSkill.武器构想)}({sheet.GetRowOrDefault(ActionHelper.GetAdjustedActionId(PCTSkill.武器构想)).GetActionName()})");
        ImGui.Text($"风景构想变化:35349 → {ActionHelper.GetAdjustedActionId(PCTSkill.风景构想)}({sheet.GetRowOrDefault(ActionHelper.GetAdjustedActionId(PCTSkill.风景构想)).GetActionName()})");
    }

    private void DrawDev3()
    {
        ImGui.Text($"武器构想次数:{PCTSkill.武器构想.GetActionCharges()}");
        ImGui.Text($"动物构想次数:{PCTSkill.动物构想.GetActionCharges()}");
        ImGui.Text($"武器构想总CD:{PCTSkill.武器构想.GetActionCooldown()}");
        ImGui.Text($"动物构想层数:{PctHelper.GetCurrentCreatureCharge()}");
        ImGui.Text($"减色混合次数:{PctHelper.GetCurrentReverseCharge()}");
    }
}
