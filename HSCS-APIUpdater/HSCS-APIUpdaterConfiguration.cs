using JetBrains.Annotations;

namespace HSCS_APIUpdater;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class HSCS_APIUpdaterConfiguration {
    public string? ServerAddress { get; set; }
    public string? APIKey { get; set; }
    public int? ServerPort { get; set; }
}