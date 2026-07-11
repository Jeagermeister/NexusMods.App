
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.Games.CreationEngine.Abstractions;

namespace Apocrypha.Games.CreationEngine.SkyrimSE;

public class SkyrimSESynchronizer(IServiceProvider provider, ICreationEngineGame game) : ACreationEngineSynchronizer(provider, game);
