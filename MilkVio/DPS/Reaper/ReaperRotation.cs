using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using ECommons.Logging;
using PromeRotation.Data;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using MilkVio.DPS.Reaper.Action.Gcd;
using MilkVio.DPS.Reaper.ReaperData;
using PromeRotation.Timeline;
using PromeRotation.Windows;

namespace MilkVio.DPS.Reaper;

[RotationMetadata((uint)Job.RPR, "随便写的钐镰客", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public class ReaperRotation// : IRotation
{
    private readonly IRotationEventHandler _eventHandler = new ReaperRotationEventHandler();
    public IRotationEventHandler GetEventHandler() => _eventHandler;

    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();

    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        {ReaperQt.启用起手, true},
        {ReaperQt.AOE, false},
        {ReaperQt.倾泻资源, false},
        
        {ReaperQt.神秘环, true},
        {ReaperQt.附体, true},
        {ReaperQt.完人, true},
        
        {ReaperQt.灵魂割, true},
        {ReaperQt.暴食, true},
        {ReaperQt.隐匿挥割, true},
        
        {ReaperQt.收获月, true},
        {ReaperQt.勾刃, true},
    };
    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {

    };

    public ReaperRotation()
    {
        // 在这里，按照优先级从高到低的顺序，注册所有的解析器
        // 爆发相关的oGCD优先级最高
        // _offGcdResolvers.Add(new ReaperOffGcd());

        // 爆发状态下的GCD
        _gcdResolvers.Add(new 绞决缢杀Gcd());
        _gcdResolvers.Add(new 基础连击Gcd());

        // 画QT
        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
    }

        public IOpener? GetOpener()
        {
            if (!PromeSettings.Instance.GetQt(ReaperQt.启用起手) && Core.Me == null)
                return null;

            var openerName = PromeRotation.PureTimeline.PtlManager.CurrentOpener;
            var openerSource = "PureTimeline";

            if (string.IsNullOrWhiteSpace(openerName))
            {
                var meta = TimelineManager.CurrentMeta;
                openerName = meta?.Opener;
                openerSource = "Timeline";
            }

            if (string.IsNullOrWhiteSpace(openerName))
                return null;

            var openers = RotationManager.GetOpenersByJob((int)Core.Me.ClassJob.RowId);
            if (openers == null || !openers.TryGetValue(openerName, out var openerType))
            {
                PluginLog.Warning($"[ACR] {openerSource} 指定起手不存在：{openerName}");
                return null;
            }

            try
            {
                if (Activator.CreateInstance(openerType) is IOpener opener)
                {
                    PluginLog.Information($"[ACR] 从{openerSource} Meta 加载起手：{openerName}");
                    return opener;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error($"[ACR] 创建起手实例失败: {ex.Message}");
            }

            return null;
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

    }

    private void DrawDev()
    {

    }

}
