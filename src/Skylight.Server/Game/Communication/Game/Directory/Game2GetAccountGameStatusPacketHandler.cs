using Net.Communication.Attributes;
using Skylight.API.Game.Users;
using Skylight.Protocol.Packets.Incoming.Game.Directory;
using Skylight.Protocol.Packets.Manager;

namespace Skylight.Server.Game.Communication.Game.Directory;

[PacketManagerRegister(typeof(AbstractGamePacketManager))]
internal sealed class Game2GetAccountGameStatusPacketHandler<T> : UserPacketHandler<T>
	where T : IGame2GetAccountGameStatusIncomingPacket
{
	internal override void Handle(IUser user, in T packet)
	{
	}
}
