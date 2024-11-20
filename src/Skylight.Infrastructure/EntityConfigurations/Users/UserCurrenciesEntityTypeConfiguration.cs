using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skylight.Domain.Users;

namespace Skylight.Infrastructure.EntityConfigurations.Users;

internal sealed class UserCurrenciesEntityTypeConfiguration : IEntityTypeConfiguration<UserCurrenciesEntity>
{
	public void Configure(EntityTypeBuilder<UserCurrenciesEntity> builder)
	{
		builder.ToTable("user_currencies");

		builder.HasKey(u => new { u.UserId, u.Currency });

		builder.HasOne(u => u.User)
			.WithMany()
			.HasForeignKey(u => u.UserId);

		builder.Property(u => u.Currency)
			.HasMaxLength(100);

		builder.Property(u => u.Balance)
			.HasDefaultValue(0.00m);
	}
}
