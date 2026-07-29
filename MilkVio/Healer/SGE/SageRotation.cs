using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using ECommons.Logging;
using MilkVio.Healer.Common;
using MilkVio.Healer.SGE.Openers;
using MilkVio.Healer.SGE;
using MilkVio.Healer.SGE.Resolvers.Gcd;
using MilkVio.Healer.SGE.Resolvers.OffGcd;
using MilkVio.Healer.SGE.Timeline;
using PromeRotation.Data;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using PromeRotation.Timeline;
using PromeRotation.Timeline.Core;

namespace MilkVio.Healer.SGE;

[RotationMetadata((uint)Job.SGE, "Mio Sage", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public sealed class SageRotation : IRotation
{
    private readonly IRotationEventHandler _eventHandler = new DefaultRotationEventHandler();
    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();
    private readonly SageRotationContext _context = new();

    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        { HealerQt.启用起手, true },
        { HealerQt.停手, false },
        { HealerQt.AOE, true },
        { HealerQt.DOT, true },
        { HealerQt.康复, true },
        { HealerQt.醒梦, true },
        { HealerQt.发炎, true },
        { HealerQt.箭毒, true },
        { HealerQt.心神风息, true },
    };

    private static readonly string[] OpenerNames =
    [
        均衡魂灵风息起手.OpenerDisplayName,
        注药均衡起手.OpenerDisplayName,
        均衡箭毒起手.OpenerDisplayName,
        群盾注药均衡起手.OpenerDisplayName
    ];

    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        { OpenerNames[0], typeof(均衡魂灵风息起手) },
        { OpenerNames[1], typeof(注药均衡起手) },
        { OpenerNames[2], typeof(均衡箭毒起手) },
        { OpenerNames[3], typeof(群盾注药均衡起手) },
    };

    public static IJobNodeProvider? NodeProvider { get; } = new SageJobNodeProvider();

    public SageRotation()
    {
        // 在这里，按照优先级从高到低注册贤者 oGCD 解析器。
        _offGcdResolvers.Add(new 醒梦OffGcd());
        _offGcdResolvers.Add(new 心神风息OffGcd());

        // 在这里，按照优先级从高到低注册贤者 GCD 解析器。
        _gcdResolvers.Add(new 康复Gcd(_context));
        _gcdResolvers.Add(new 发炎Gcd());
        _gcdResolvers.Add(new 箭毒Gcd());
        _gcdResolvers.Add(new 均衡注药Gcd());
        _gcdResolvers.Add(new 均衡失衡Gcd());
        _gcdResolvers.Add(new 失衡Gcd());
        _gcdResolvers.Add(new 注药Gcd());

        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
    }

    public IRotationEventHandler GetEventHandler() => _eventHandler;

    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(HealerQt.启用起手) || Core.Me == null)
            return null;

        if (TryCreateTimelineOpener(out var timelineOpener))
            return timelineOpener;

        return CreateOpener(SelectedOpenerName(), "设置页");
    }

    private static bool TryCreateTimelineOpener(out IOpener? opener)
    {
        var openerName = PromeRotation.PureTimeline.PtlManager.CurrentOpener;
        var openerSource = "PureTimeline";

        if (string.IsNullOrWhiteSpace(openerName))
        {
            var meta = TimelineManager.CurrentMeta;
            openerName = meta?.Opener;
            openerSource = "Timeline";
        }

        if (string.IsNullOrWhiteSpace(openerName))
        {
            opener = null;
            return false;
        }

        opener = CreateOpener(openerName, openerSource);
        return opener != null;
    }

    private static IOpener? CreateOpener(string openerName, string openerSource)
    {
        if (!Openers.TryGetValue(openerName, out var openerType))
        {
            PluginLog.Warning($"[ACR] {openerSource} 指定起手不存在：{openerName}");
            return null;
        }

        try
        {
            if (Activator.CreateInstance(openerType) is IOpener opener)
            {
                PluginLog.Information($"[ACR] 从{openerSource}加载起手：{openerName}");
                return opener;
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[ACR] 创建起手实例失败: {ex.Message}");
        }

        return null;
    }

    public PAction? NextAlways() => Resolve(_alwaysResolvers);

    public PAction? NextGcd() => Resolve(_gcdResolvers);

    public PAction? NextOffGcd() => Resolve(_offGcdResolvers);

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

    public void DrawQTs() => DrawGeneral();

    public void DrawSettings()
    {
        if (!ImGui.BeginTabBar($"Settings##{nameof(SageRotation)}")) return;

        if (ImGui.BeginTabItem("设置"))
        {
            DrawOpenerSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("开发用"))
        {
            DrawDev();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static string SelectedOpenerName()
    {
        var selectedIndex = Math.Clamp(MioAcrSettings.Instance.SageOpenerIndex, 0, OpenerNames.Length - 1);
        return OpenerNames[selectedIndex];
    }

    private static void DrawOpenerSettings()
    {
        var selectedIndex = Math.Clamp(MioAcrSettings.Instance.SageOpenerIndex, 0, OpenerNames.Length - 1);
        var selectedName = SelectedOpenerName();
        if (!ImGui.BeginCombo("贤者起手", selectedName)) return;

        for (var i = 0; i < OpenerNames.Length; i++)
        {
            var isSelected = i == selectedIndex;
            if (ImGui.Selectable(OpenerNames[i], isSelected))
            {
                MioAcrSettings.Instance.SageOpenerIndex = i;
                MioAcrSettings.Save();
            }

            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawGeneral()
    {
        foreach (var (name, _) in QtList)
        {
            var value = PromeSettings.Instance.GetQt(name);
            if (ImGui.Checkbox(name, ref value))
                PromeSettings.Instance.SetQt(name, value);
        }
    }

    private void DrawDev()
    {
        var me = HealerUtils.Me;
        ImGui.Text($"Job:{(uint)Job.SGE}");
        ImGui.Text($"Level:{me?.Level ?? 0}");
        ImGui.Text($"MP:{me?.CurrentMp ?? 0}");
        ImGui.Text($"Addersgall:{HealerUtils.SgeAddersgall()}");
        ImGui.Text($"Addersting:{HealerUtils.SgeAddersting()}");
        ImGui.Text($"Eukrasia:{HealerUtils.SgeEukrasia()}");
        ImGui.Text($"AddersgallTimer:{HealerUtils.SgeAddersgallTimer()}");
    }

    private static PAction? Resolve(IEnumerable<IDecisionResolver> resolvers)
    {
        foreach (var resolver in resolvers)
        {
            if (resolver.Check().Success)
                return resolver.GetAction();
        }

        return null;
    }
}
