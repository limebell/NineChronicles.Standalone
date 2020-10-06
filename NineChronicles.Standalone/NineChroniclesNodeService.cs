using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bencodex.Types;
using Grpc.Core;
using Lib9c.Renderer;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blockchain.Renderers;
using Libplanet.Crypto;
using Libplanet.Net;
using Libplanet.Standalone.Hosting;
using MagicOnion.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nekoyume.Action;
using Nekoyume.BlockChain;
using Nekoyume.Model.State;
using NineChronicles.Standalone.Properties;
using Nito.AsyncEx;
using Serilog;
using Serilog.Events;
using NineChroniclesActionType = Libplanet.Action.PolymorphicAction<Nekoyume.Action.ActionBase>;

namespace NineChronicles.Standalone
{
    public class NineChroniclesNodeService
    {
        private LibplanetNodeService<NineChroniclesActionType> NodeService { get; set; }

        private LibplanetNodeServiceProperties<NineChroniclesActionType> Properties { get; }

        private RpcNodeServiceProperties? RpcProperties { get; }

        public BlockRenderer BlockRenderer { get; }

        public ActionRenderer ActionRenderer { get; }

        public ExceptionRenderer ExceptionRenderer { get; }

        public AsyncManualResetEvent BootstrapEnded => NodeService.BootstrapEnded;

        public AsyncManualResetEvent PreloadEnded => NodeService.PreloadEnded;

        public Swarm<NineChroniclesActionType> Swarm => NodeService?.Swarm;

        public PrivateKey PrivateKey { get; set; }

        public NineChroniclesNodeService(
            LibplanetNodeServiceProperties<NineChroniclesActionType> properties,
            RpcNodeServiceProperties? rpcNodeServiceProperties,
            Progress<PreloadState> preloadProgress = null,
            bool ignoreBootstrapFailure = false,
            bool strictRendering = false,
            bool isDev = false,
            int blockInterval = 10,
            int reorgInterval = 0
        )
        {
            Properties = properties;
            RpcProperties = rpcNodeServiceProperties;

            try
            {
                Libplanet.Crypto.CryptoConfig.CryptoBackend = new Secp256K1CryptoBackend<SHA256>();
                Log.Debug("Secp256K1CryptoBackend initialized.");
            }
            catch(Exception e)
            {
                Log.Error("Secp256K1CryptoBackend initialize failed. Use default backend. {e}", e);
            }

            var blockPolicySource = new BlockPolicySource(Log.Logger, LogEventLevel.Debug);
            // BlockPolicy shared through Lib9c.
            IBlockPolicy<PolymorphicAction<ActionBase>> blockPolicy = null;
            // Policies for dev mode.
            IBlockPolicy<PolymorphicAction<ActionBase>> easyPolicy = null;
            IBlockPolicy<PolymorphicAction<ActionBase>> hardPolicy = null;
            if (isDev)
            {
                easyPolicy = new ReorgPolicy(new RewardGold(), 1);
                hardPolicy = new ReorgPolicy(new RewardGold(), 2);
            }
            else
            {
                blockPolicy = blockPolicySource.GetPolicy(properties.MinimumDifficulty);
            }

            BlockRenderer = blockPolicySource.BlockRenderer;
            ActionRenderer = blockPolicySource.ActionRenderer;
            ExceptionRenderer = new ExceptionRenderer();
            var renderers = new List<IRenderer<NineChroniclesActionType>>();
            if (Properties.Render)
            {
                renderers.Add(blockPolicySource.BlockRenderer);
                renderers.Add(blockPolicySource.LoggedActionRenderer);
            }
            else
            {
                renderers.Add(blockPolicySource.LoggedBlockRenderer);
            }

            if (strictRendering)
            {
                renderers.Add(new ValidatingActionRenderer<NineChroniclesActionType>(blockPolicy ?? easyPolicy));
            }

            async Task minerLoopAction(
                BlockChain<NineChroniclesActionType> chain,
                Swarm<NineChroniclesActionType> swarm,
                PrivateKey privateKey,
                CancellationToken cancellationToken)
            {
                var miner = new Miner(chain, swarm, privateKey);
                Log.Debug("Miner called.");
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (swarm.Running)
                        {
                            Log.Debug("Start mining.");
                            var block = await miner.MineBlockAsync(cancellationToken);

                            const int txCountThreshold = 10;
                            var txCount = block?.Transactions.Count() ?? 0;
                            if (!(block is null) && txCount >= txCountThreshold)
                            {
                                Log.Error($"Block {block.Index}({block.Hash}) transaction count is {txCount}.");
                            }
                        }
                        else
                        {
                            await Task.Delay(1000, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Exception occurred.");
                    }
                }
            }

            async Task devMinerLoopAction(
                Swarm<NineChroniclesActionType> mainSwarm,
                Swarm<NineChroniclesActionType> subSwarm,
                PrivateKey privateKey,
                CancellationToken cancellationToken)
            {
                var miner = new ReorgMiner(mainSwarm, subSwarm, privateKey, reorgInterval);
                Log.Debug("Miner called.");
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (mainSwarm.Running)
                        {
                            Log.Debug("Start mining.");
                            var (mainBlock, subBlock) = await miner.MineBlockAsync(cancellationToken);
                            await Task.Delay(blockInterval * 1000, cancellationToken);

                            const int txCountThreshold = 10;
                            var txCount = mainBlock?.Transactions.Count() ?? 0;
                            if (!(mainBlock is null) && txCount >= txCountThreshold)
                            {
                                Log.Error($"Block {mainBlock.Index}({mainBlock.Hash}) transaction count is {txCount}.");
                            }
                        }
                        else
                        {
                            await Task.Delay(1000, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Exception occurred.");
                    }
                }
            }

            if (isDev)
            {
                NodeService = new DevLibplanetNodeService<NineChroniclesActionType>(
                    Properties,
                    easyPolicy,
                    hardPolicy,
                    renderers,
                    devMinerLoopAction,
                    preloadProgress,
                    (code, msg) =>
                    {
                        ExceptionRenderer.RenderException(code, msg);
                        Log.Error(msg);
                    },
                    ignoreBootstrapFailure
                );
            }
            else
            {
                NodeService = new LibplanetNodeService<NineChroniclesActionType>(
                    Properties,
                    blockPolicy,
                    renderers,
                    minerLoopAction,
                    preloadProgress,
                    (code, msg) =>
                    {
                        ExceptionRenderer.RenderException(code, msg);
                        Log.Error(msg);
                    },
                    ignoreBootstrapFailure
                );
            }

            if (NodeService?.BlockChain?.GetState(AuthorizedMinersState.Address) is Dictionary ams &&
                blockPolicy is BlockPolicy bp)
            {
                bp.AuthorizedMinersState = new AuthorizedMinersState(ams);
            }
        }

        public IHostBuilder Configure(IHostBuilder hostBuilder)
        {
            if (RpcProperties is RpcNodeServiceProperties rpcProperties)
            {
                hostBuilder = hostBuilder
                    .UseMagicOnion(
                        new ServerPort(rpcProperties.RpcListenHost, rpcProperties.RpcListenPort, ServerCredentials.Insecure)
                    )
                    .ConfigureServices((ctx, services) =>
                    {
                        services.AddHostedService(provider => new ActionEvaluationPublisher(
                            BlockRenderer,
                            ActionRenderer,
                            ExceptionRenderer,
                            IPAddress.Loopback.ToString(),
                            rpcProperties.RpcListenPort
                        ));
                    });
            }

            return hostBuilder.ConfigureServices((ctx, services) =>
            {
                services.AddHostedService(provider => NodeService);
                services.AddSingleton(provider => NodeService.Swarm);
                services.AddSingleton(provider => NodeService.BlockChain);
            });
        }

        public void StartMining() => NodeService.StartMining(PrivateKey);

        public void StopMining() => NodeService.StopMining();
        
        public Task<bool> CheckPeer(string addr) => NodeService.CheckPeer(addr);
    }
}
