using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Skylight.API.Game.Badges;
using Skylight.API.Game.Catalog;
using Skylight.API.Game.Furniture;
using Skylight.API.Game.Furniture.Floor;
using Skylight.API.Game.Furniture.Wall;
using Skylight.API.Game.Inventory.Items;
using Skylight.API.Game.Users;
using Skylight.Domain.Badges;
using Skylight.Domain.Items;
using Skylight.Domain.Users;
using Skylight.Infrastructure;
using Skylight.Server.Extensions;
using Skylight.Server.Game.Inventory.Items.Badges;

namespace Skylight.Server.Game.Catalog;

internal sealed class CatalogTransaction : ICatalogTransaction
{
	private readonly IFurnitureSnapshot furnitures;
	private readonly IFurnitureInventoryItemStrategy furnitureInventoryItemStrategy;

	private readonly SkylightContext dbContext;
	private readonly IDbContextTransaction transaction;

	private readonly IUser user;

	public string ExtraData { get; }

	private List<IBadge>? badges;

	private List<FloorItemEntity>? floorItems;
	private List<WallItemEntity>? wallItems;

	private Dictionary<string, decimal> currencyChanges = new();

	internal CatalogTransaction(IFurnitureSnapshot furnitures, IFurnitureInventoryItemStrategy furnitureInventoryItemStrategy, SkylightContext dbContext, IDbContextTransaction transaction, IUser user, string extraData)
	{
		this.furnitures = furnitures;
		this.furnitureInventoryItemStrategy = furnitureInventoryItemStrategy;

		this.dbContext = dbContext;
		this.transaction = transaction;

		this.user = user;

		this.ExtraData = extraData;
	}

	public DbTransaction Transaction => this.transaction.GetDbTransaction();

	public void AddBadge(IBadge badge)
	{
		if (this.user.Inventory.HasBadge(badge.Code))
		{
			return;
		}

		UserBadgeEntity entity = new()
		{
			UserId = this.user.Profile.Id,
			BadgeCode = badge.Code
		};

		this.badges ??= [];
		this.badges.Add(badge);
		this.dbContext.Add(entity);
	}

	public void AddFloorItem(IFloorFurniture furniture, JsonDocument? extraData)
	{
		Debug.Assert(this.furnitures.TryGetFloorFurniture(furniture.Id, out IFloorFurniture? debugFurniture) && debugFurniture == furniture);

		FloorItemEntity entity = new()
		{
			FurnitureId = furniture.Id,
			UserId = this.user.Profile.Id
		};

		if (extraData is not null)
		{
			entity.Data = new FloorItemDataEntity
			{
				ExtraData = extraData
			};
		}

		this.floorItems ??= [];
		this.floorItems.Add(entity);
		this.dbContext.Add(entity);
	}

	public void AddWallItem(IWallFurniture furniture, JsonDocument? extraData)
	{
		Debug.Assert(this.furnitures.TryGetWallFurniture(furniture.Id, out IWallFurniture? debugFurniture) && debugFurniture == furniture);

		WallItemEntity entity = new()
		{
			FurnitureId = furniture.Id,
			UserId = this.user.Profile.Id
		};

		if (extraData is not null)
		{
			entity.Data = new WallItemDataEntity
			{
				ExtraData = extraData
			};
		}

		this.wallItems ??= [];
		this.wallItems.Add(entity);
		this.dbContext.Add(entity);
	}

	public void DeductCurrency(string currencyKey, decimal amount)
	{
		decimal currentBalance = this.user.Currencies.GetBalance(currencyKey);

		if (currentBalance < amount)
		{
			throw new InvalidOperationException("Not enough balance to complete the purchase.");
		}

		decimal newBalance = currentBalance - amount;
		this.user.Currencies.UpdateBalance(currencyKey, newBalance);

		this.currencyChanges[currencyKey] = newBalance;
	}

	public async Task CompleteAsync(CancellationToken cancellationToken)
	{
		foreach (KeyValuePair<string, decimal> currency in this.currencyChanges)
		{
			UserCurrenciesEntity? userCurrencyEntity = await this.dbContext.UserCurrencies
				.FirstOrDefaultAsync(c => c.UserId == this.user.Profile.Id && c.Currency == currency.Key, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (userCurrencyEntity != null)
			{
				userCurrencyEntity.Balance = currency.Value;
				this.dbContext.UserCurrencies.Update(userCurrencyEntity);
			}
			else
			{
				await this.dbContext.UserCurrencies.AddAsync(new UserCurrenciesEntity
				{
					UserId = this.user.Profile.Id,
					Currency = currency.Key,
					Balance = currency.Value
				}, cancellationToken).ConfigureAwait(false);
			}
		}

		await this.dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		await this.transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	public void Dispose() => this.DisposeAsync().Wait();

	public async ValueTask DisposeAsync()
	{
		await this.dbContext.DisposeAsync().ConfigureAwait(false);
		await this.transaction.DisposeAsync().ConfigureAwait(false);

		List<IInventoryItem> items = [];
		if (this.badges is not null)
		{
			foreach (IBadge badge in this.badges)
			{
				items.Add(new BadgeInventoryItem(badge, this.user.Profile));
			}
		}

		if (this.floorItems is not null)
		{
			foreach (FloorItemEntity item in this.floorItems)
			{
				this.furnitures.TryGetFloorFurniture(item.FurnitureId, out IFloorFurniture? furniture);

				items.Add(this.furnitureInventoryItemStrategy.CreateFurnitureItem(item.Id, this.user.Profile, furniture!, item.Data?.ExtraData));
			}
		}

		if (this.wallItems is not null)
		{
			foreach (WallItemEntity item in this.wallItems)
			{
				this.furnitures.TryGetWallFurniture(item.FurnitureId, out IWallFurniture? furniture);

				items.Add(this.furnitureInventoryItemStrategy.CreateFurnitureItem(item.Id, this.user.Profile, furniture!, item.Data?.ExtraData));
			}
		}

		this.user.Inventory.AddUnseenItems(items);
	}
}
