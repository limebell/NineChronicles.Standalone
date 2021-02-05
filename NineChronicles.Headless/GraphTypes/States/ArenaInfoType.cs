using GraphQL.Types;
using Nekoyume.Model.State;

namespace NineChronicles.Headless.GraphTypes.States
{
    public class ArenaInfoType : ObjectGraphType<ArenaInfo>
    {
        public ArenaInfoType()
        {
            Field<NonNullGraphType<AddressType>>(
                nameof(ArenaInfo.AvatarAddress),
                resolve: context => context.Source.AvatarAddress);
            Field<NonNullGraphType<AddressType>>(
                nameof(ArenaInfo.AgentAddress),
                resolve: context => context.Source.AgentAddress);
            Field<NonNullGraphType<StringGraphType>>(
                nameof(ArenaInfo.AvatarName),
                resolve: context => context.Source.AvatarName);
            Field<NonNullGraphType<ArenaRecordType>>(
                nameof(ArenaInfo.ArenaRecord),
                resolve: context => context.Source.ArenaRecord);
            Field<NonNullGraphType<IntGraphType>>(
                nameof(ArenaInfo.Level),
                resolve: context => context.Source.Level);
            Field<NonNullGraphType<IntGraphType>>(
                nameof(ArenaInfo.CombatPoint),
                resolve: context => context.Source.CombatPoint);
            Field<NonNullGraphType<IntGraphType>>(
                nameof(ArenaInfo.ArmorId),
                resolve: context => context.Source.ArmorId);
            Field<NonNullGraphType<BooleanGraphType>>(
                nameof(ArenaInfo.Active),
                resolve: context => context.Source.Active);
            Field<NonNullGraphType<IntGraphType>>(
                nameof(ArenaInfo.DailyChallengeCount),
                resolve: context => context.Source.DailyChallengeCount);
            Field<NonNullGraphType<IntGraphType>>(
                nameof(ArenaInfo.Score),
                resolve: context => context.Source.Score);
        }
    }
}