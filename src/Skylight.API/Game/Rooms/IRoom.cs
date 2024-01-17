﻿using Skylight.API.Game.Rooms.Items;
using Skylight.API.Game.Rooms.Map;
using Skylight.API.Game.Rooms.Units;
using Skylight.API.Game.Users;
using Skylight.Protocol.Packets.Outgoing;

namespace Skylight.API.Game.Rooms;

public interface IRoom
{
	public IRoomInfo Info { get; }

	public IRoomMap Map { get; }

	public IRoomItemManager ItemManager { get; }
	public IRoomUnitManager UnitManager { get; }

	public int GameTime { get; }

	public ValueTask SendAsync<T>(in T packet)
		where T : IGameOutgoingPacket;

	public Task LoadAsync(CancellationToken cancellationToken = default);

	public void Enter(IUser user);
	public void Exit(IUser user);

	public bool PostTask<TTask>(TTask task)
		where TTask : IRoomTask;

	public ValueTask PostTaskAsync<TTask>(TTask task)
		where TTask : IRoomTask;

	public ValueTask<TResult> ScheduleTask<TTask, TResult>(TTask task)
		where TTask : IRoomTask<TResult>;

	public ValueTask<TResult> ScheduleTaskAsync<TTask, TResult>(TTask task)
		where TTask : IAsyncRoomTask<TResult>;

	public bool PostTask(Action<IRoom> action) => throw new NotSupportedException();
	public ValueTask PostTaskAsync(Action<IRoom> action) => throw new NotSupportedException();
	public ValueTask<TReturn> ScheduleTask<TReturn>(Func<IRoom, TReturn> func) => throw new NotSupportedException();
	public ValueTask<TResult> ScheduleTaskAsync<TResult>(Func<IRoom, ValueTask<TResult>> func) => throw new NotSupportedException();

	public void ScheduleUpdateTask(IRoomTask task);
}
