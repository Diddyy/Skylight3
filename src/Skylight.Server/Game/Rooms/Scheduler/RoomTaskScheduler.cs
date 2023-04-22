﻿using System.Threading.Channels;
using CommunityToolkit.HighPerformance;
using Skylight.API.Game.Rooms;
using Skylight.Server.Extensions;
using Skylight.Server.Game.Rooms.Scheduler.Tasks;

namespace Skylight.Server.Game.Rooms.Scheduler;

internal sealed class RoomTaskScheduler
{
	private readonly Room room;

	private readonly Channel<IRoomTask> scheduledTasks;

	private SpinLock scheduledTasksLock; //Note: mutating struct

	private readonly RoomSynchronizationContext synchronizationContext;

	internal RoomTaskScheduler(Room room)
	{
		this.room = room;

		this.scheduledTasks = Channel.CreateUnbounded<IRoomTask>(new UnboundedChannelOptions
		{
			SingleReader = true
		});

		this.scheduledTasksLock = new SpinLock(enableThreadOwnerTracking: false);

		this.synchronizationContext = new RoomSynchronizationContext(this);
	}

	public bool ScheduleTask<TTask>(in TTask task)
		where TTask : IRoomTask => this.ScheduleTaskInternal<RawRoomTaskScheduler<TTask>, bool>(new RawRoomTaskScheduler<TTask>(task));

	public bool ScheduleTask(Action<IRoom> action) => this.ScheduleTask(static (room, state) => state(room), action);
	public bool ScheduleTask<TState>(Action<IRoom, TState> action, in TState state) => this.ScheduleTaskInternal<ActionRoomTaskScheduler<TState>, bool>(new ActionRoomTaskScheduler<TState>(action, state));

	public ValueTask ScheduleTaskAsync<TTask>(in TTask task)
		where TTask : IRoomTask
	{
		return this.ScheduleTaskAsync(static (room, state) =>
		{
			state.Execute(room);

			return ValueTask.CompletedTask;
		}, task);
	}

	public ValueTask ScheduleTaskAsync(Action<IRoom> action)
	{
		return this.ScheduleTaskAsync(static (room, state) =>
		{
			state(room);

			return ValueTask.CompletedTask;
		}, action);
	}

	public ValueTask ScheduleTaskAsync<TState>(Action<IRoom, TState> action, in TState state)
	{
		return this.ScheduleTaskAsync(static (room, state) =>
		{
			state.action(room, state.state);

			return ValueTask.CompletedTask;
		}, (action, state));
	}

	public ValueTask ScheduleTaskAsync(Func<IRoom, ValueTask> func) => this.ScheduleTaskAsync(static (room, state) => state(room), func);
	public ValueTask ScheduleTaskAsync<TState>(Func<IRoom, TState, ValueTask> func, in TState state) => this.ScheduleTaskInternal<FuncRoomTaskScheduler<TState>, ValueTask>(new FuncRoomTaskScheduler<TState>(func, state));

	public ValueTask<TOut> ScheduleTaskAsync<TOut>(Func<IRoom, TOut> func) => this.ScheduleTaskAsync(static (room, state) => ValueTask.FromResult(state(room)), func);
	public ValueTask<TOut> ScheduleTaskAsync<TState, TOut>(Func<IRoom, TState, TOut> func, in TState state) => this.ScheduleTaskAsync(static (room, state) => ValueTask.FromResult(state.func(room, state.state)), (func, state));

	public ValueTask<TOut> ScheduleTaskAsync<TOut>(Func<IRoom, ValueTask<TOut>> func) => this.ScheduleTaskAsync(static (room, state) => state(room), func);
	public ValueTask<TOut> ScheduleTaskAsync<TState, TOut>(Func<IRoom, TState, ValueTask<TOut>> func, in TState state) => this.ScheduleTaskInternal<FuncRoomTaskScheduler<TState, TOut>, ValueTask<TOut>>(new FuncRoomTaskScheduler<TState, TOut>(func, state));

	private TOut ScheduleTaskInternal<TTask, TOut>(in TTask action)
		where TTask : IRoomTaskScheduler<TOut>
	{
		if (this.room.TickingLock.TryEnter())
		{
			SynchronizationContext? context = SynchronizationContext.Current;

			try
			{
				SynchronizationContext.SetSynchronizationContext(this.synchronizationContext);

				return action.Execute(this.room);
			}
			finally
			{
				SynchronizationContext.SetSynchronizationContext(context);

				this.room.TickingLock.Exit();
			}
		}
		else
		{
			return action.CreateTask(this.room);
		}
	}

	private bool ScheduleTaskSlow(IRoomTask task)
	{
		//Schedule the task to be run after ticking lock releases
		if (!this.scheduledTasks.Writer.TryWrite(task))
		{
			//We are disposing, nothing to run anymore
			return false;
		}

		//Are we currently executing the tasks?
		if (this.scheduledTasksLock.TryEnter())
		{
			try
			{
				//We are not currently executing tasks, but are we still ticking?
				if (this.room.TickingLock.TryEnter())
				{
					//We aren't ticking, try to run all of the schedules tasks
					try
					{
						this.ExecuteTasksNoLock();
					}
					finally
					{
						this.room.TickingLock.Exit();
					}
				}
				else
				{
					//We are still ticking, the scheduled tasks will be run as soon as its done
				}
			}
			finally
			{
				this.scheduledTasksLock.Exit();
			}
		}
		else
		{
			//We are currently executing tasks, try to enter to the lock
			//to ensure we run the task asap as we might already be
			//exiting the loop while posted the task
			using (this.room.TickingLock.Enter())
			{
				using (this.scheduledTasksLock.Enter())
				{
					this.ExecuteTasksNoLock();
				}
			}
		}

		return true;
	}

	private void ExecuteTasksNoLock()
	{
		SynchronizationContext? context = SynchronizationContext.Current;

		try
		{
			SynchronizationContext.SetSynchronizationContext(this.synchronizationContext);

			while (this.scheduledTasks.Reader.TryRead(out IRoomTask? task))
			{
				task.Execute(this.room);
			}
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(context);
		}
	}

	internal void ExecuteTasks()
	{
		using (this.scheduledTasksLock.Enter())
		{
			this.ExecuteTasksNoLock();
		}
	}

	private readonly struct RawRoomTaskScheduler<TTask>(TTask task) : IRoomTaskScheduler<bool>
		where TTask : IRoomTask
	{
		public bool CreateTask(Room room)
		{
			task.Execute(room);

			return true;
		}

		public bool Execute(Room room) => room.RoomTaskScheduler.ScheduleTaskSlow(task);
	}

	private readonly struct ActionRoomTaskScheduler<TState>(Action<IRoom, TState> action, TState state) : IRoomTaskScheduler<bool>
	{
		public bool CreateTask(Room room)
		{
			action(room, state);

			return true;
		}

		public bool Execute(Room room) => room.RoomTaskScheduler.ScheduleTaskSlow(new ActionRoomTask<TState>(action, state));
	}

	private readonly struct FuncRoomTaskScheduler<TState>(Func<IRoom, TState, ValueTask> func, TState state) : IRoomTaskScheduler<ValueTask>
	{
		public ValueTask CreateTask(Room room) => func(room, state);
		public ValueTask Execute(Room room)
		{
			AsyncFuncRoomTask<TState> task = new(func, state);

			room.RoomTaskScheduler.ScheduleTaskSlow(task);

			return new ValueTask(task.Task);
		}
	}

	private readonly struct FuncRoomTaskScheduler<TState, TOut>(Func<IRoom, TState, ValueTask<TOut>> func, TState state) : IRoomTaskScheduler<ValueTask<TOut>>
	{
		public ValueTask<TOut> CreateTask(Room room) => func(room, state);
		public ValueTask<TOut> Execute(Room room)
		{
			AsyncFuncRoomTask<TState, TOut> task = new(func, state);

			room.RoomTaskScheduler.ScheduleTaskSlow(task);

			return new ValueTask<TOut>(task.Task);
		}
	}
}