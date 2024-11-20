﻿using System.Data.Common;
using System.Text.Json;
using Skylight.API.Game.Badges;
using Skylight.API.Game.Furniture.Floor;
using Skylight.API.Game.Furniture.Wall;

namespace Skylight.API.Game.Catalog;

public interface ICatalogTransaction : IAsyncDisposable, IDisposable
{
	public DbTransaction Transaction { get; }

	public string ExtraData { get; }

	public void AddBadge(IBadge badge);

	public void AddFloorItem(IFloorFurniture furniture, JsonDocument? extraData);
	public void AddWallItem(IWallFurniture furniture, JsonDocument? extraData);

	public void DeductCurrency(string currency, decimal amount);

	public Task CompleteAsync(CancellationToken cancellationToken = default);
}
