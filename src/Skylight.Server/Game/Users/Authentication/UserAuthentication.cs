using Microsoft.EntityFrameworkCore;
using Skylight.API.Game.Badges;
using Skylight.API.Game.Clients;
using Skylight.API.Game.Furniture;
using Skylight.API.Game.Inventory.Items;
using Skylight.API.Game.Users;
using Skylight.API.Game.Users.Authentication;
using Skylight.Domain.Users;
using Skylight.Infrastructure;
using Skylight.Server.Redis;
using StackExchange.Redis;

namespace Skylight.Server.Game.Users.Authentication;

internal sealed class UserAuthentication(RedisConnector redis, IDbContextFactory<SkylightContext> dbContextFactory, IUserManager userManager, IBadgeManager badgeManager, IFurnitureManager furnitureManager, IFurnitureInventoryItemStrategy furnitureInventoryItemFactory)
	: IUserAuthentication
{
	private static readonly RedisKey redisSsoTicketKeyPrefix = new("sso-ticket:");
	private static readonly RedisValue[] redisSsoTicketValues = ["user-id", "user-ip"];

	private readonly RedisConnector redis = redis;

	private readonly IDbContextFactory<SkylightContext> dbContextFactory = dbContextFactory;

	private readonly IUserManager userManager = userManager;

	private readonly LoadContext loadContext = new(badgeManager, furnitureManager, furnitureInventoryItemFactory);

	public Task<int?> AuthenticateAsync(IClient client, string ssoTicket, CancellationToken cancellationToken)
	{
		return Task.FromResult<int?>(1);
	}

	public async Task<int?> AuthenticateAsync(IClient client, string username, string password, CancellationToken cancellationToken = default)
	{
		await using SkylightContext dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		await dbContext.Users.Upsert(new UserEntity
		{
			Username = username
		}).On(u => u.Username)
		.WhenMatched(u => new UserEntity
		{
			LastOnline = DateTime.UtcNow
		}).RunAsync(cancellationToken).ConfigureAwait(false);

		int userId = await dbContext.Users
			.Where(u => u.Username == username)
			.Select(u => u.Id)
			.SingleAsync(cancellationToken)
			.ConfigureAwait(false);

		return userId;
	}

	public async Task<IUser?> LoginAsync(IClient client, int userId, CancellationToken cancellationToken = default)
	{
		IUserProfile? profile = await this.userManager.LoadUserProfileAsync(userId, cancellationToken).ConfigureAwait(false);
		if (profile is null)
		{
			return null;
		}

		await using SkylightContext dbContext = await this.dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		UserSettingsEntity? userSettings = await dbContext.UserSettings.FirstOrDefaultAsync(s => s.UserId == profile.Id, cancellationToken).ConfigureAwait(false);

		Dictionary<string, decimal> dbCurrencies = await dbContext.UserCurrencies
			.Where(c => c.UserId == profile.Id)
			.ToDictionaryAsync(c => c.Currency, c => c.Balance, cancellationToken)
			.ConfigureAwait(false);

		Dictionary<string, decimal> defaultCurrencies = new()
		{
			{ "skylight:credits", 0 },
			{ "skylight:silver", 0 }
		};

		foreach (KeyValuePair<string, decimal> kvp in dbCurrencies)
		{
			defaultCurrencies[kvp.Key] = kvp.Value;
		}

		User user = new(client, profile, new UserSettings(userSettings), new UserCurrencies(defaultCurrencies));

		await user.LoadAsync(dbContext, this.loadContext, cancellationToken).ConfigureAwait(false);

		return user;
	}

	internal sealed class LoadContext(IBadgeManager badgeManager, IFurnitureManager furnitureManager, IFurnitureInventoryItemStrategy furnitureInventoryItemFactory)
	{
		internal IBadgeManager BadgeManager { get; } = badgeManager;

		internal IFurnitureManager FurnitureManager { get; } = furnitureManager;
		internal IFurnitureInventoryItemStrategy FurnitureInventoryItemFactory { get; } = furnitureInventoryItemFactory;
	}
}
