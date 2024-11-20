using Net.Communication.Attributes;
using Skylight.API.Game.Users;
using Skylight.Protocol.Packets.Incoming.Collectibles;
using Skylight.Protocol.Packets.Manager;
using Skylight.Protocol.Packets.Outgoing.Collectibles;

namespace Skylight.Server.Game.Communication.Collectibles;

[PacketManagerRegister(typeof(AbstractGamePacketManager))]
internal sealed class GetSilverPacketHandler<T> : UserPacketHandler<T>
	where T : IGetSilverIncomingPacket
{
	internal override void Handle(IUser user, in T packet)
	{
		decimal silverBalance = user.Currencies.GetBalance("skylight:silver");

		user.SendAsync(new SilverBalanceOutgoingPacket((int)silverBalance));
	}
}
