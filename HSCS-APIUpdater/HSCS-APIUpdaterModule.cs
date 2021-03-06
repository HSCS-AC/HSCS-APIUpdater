using AssettoServer.Server.Plugin;
using Autofac;

namespace HSCS_APIUpdater; 

public class HSCS_APIUpdaterModule : AssettoServerModule<HSCS_APIUpdaterConfiguration> {
    protected override void Load(ContainerBuilder builder) {
        builder.RegisterType<HSCS_APIUpdater>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
    }
}