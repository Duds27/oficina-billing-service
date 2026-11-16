using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oficina.Billing.Domain;

namespace Oficina.Billing.Infrastructure.Persistence;

/// <summary>Banco do billing-service (PostgreSQL). Nenhum outro serviço acessa estas tabelas.</summary>
public sealed class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options)
    {
    }

    public DbSet<Orcamento> Orcamentos => Set<Orcamento>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<Notificacao> Notificacoes => Set<Notificacao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Orcamento>(ConfigurarOrcamento);
        modelBuilder.Entity<Pagamento>(ConfigurarPagamento);
        modelBuilder.Entity<Notificacao>(ConfigurarNotificacao);
    }

    private static void ConfigurarOrcamento(EntityTypeBuilder<Orcamento> o)
    {
        o.ToTable("Orcamentos");
        o.HasKey(x => x.Id);
        o.Property(x => x.Id).ValueGeneratedNever();
        o.Property(x => x.NumeroOS).HasMaxLength(30).IsRequired();
        o.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        o.Property(x => x.ValorTotal).HasPrecision(12, 2);
        o.Property(x => x.MotivoRecusa).HasMaxLength(500);
        o.Property(x => x.MotivoCancelamento).HasMaxLength(500);
        o.Property(x => x.Versao).IsRowVersion();

        o.OwnsOne(x => x.Cliente, c =>
        {
            c.Property(p => p.Id).HasColumnName("ClienteId");
            c.Property(p => p.Nome).HasColumnName("ClienteNome").HasMaxLength(200);
            c.Property(p => p.Email).HasColumnName("ClienteEmail").HasMaxLength(200);
        });
        o.Navigation(x => x.Cliente).IsRequired();

        o.OwnsMany(x => x.Itens, i =>
        {
            i.ToTable("ItensOrcamento");
            i.WithOwner().HasForeignKey("OrcamentoId");
            i.Property<int>("Id").UseIdentityByDefaultColumn();
            i.HasKey("Id");
            i.Property(p => p.Tipo).HasMaxLength(20);
            i.Property(p => p.Descricao).HasMaxLength(200);
            i.Property(p => p.Quantidade).HasPrecision(12, 3);
            i.Property(p => p.PrecoUnitario).HasPrecision(12, 2);
            i.Ignore(p => p.Subtotal);
        });
        o.Navigation(x => x.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Uma saga, um orçamento: o pedido repetido ou atrasado encontra o que já existe.
        o.HasIndex(x => x.CorrelationId).IsUnique();
        o.HasIndex(x => x.OsId);
    }

    private static void ConfigurarPagamento(EntityTypeBuilder<Pagamento> p)
    {
        p.ToTable("Pagamentos");
        p.HasKey(x => x.Id);
        p.Property(x => x.Id).ValueGeneratedNever();
        p.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        p.Property(x => x.Valor).HasPrecision(12, 2);
        p.Property(x => x.PreferenceId).HasMaxLength(100);
        p.Property(x => x.LinkPagamento).HasMaxLength(500);
        p.Property(x => x.MpPaymentId).HasMaxLength(50);
        p.Property(x => x.RefundId).HasMaxLength(50);
        p.Property(x => x.MotivoRecusa).HasMaxLength(500);
        p.Property(x => x.Versao).IsRowVersion();

        p.HasOne<Orcamento>().WithMany().HasForeignKey(x => x.OrcamentoId).OnDelete(DeleteBehavior.Restrict);
        p.HasIndex(x => x.OrcamentoId);
        // Um pagamento do Mercado Pago confirma uma cobrança só.
        p.HasIndex(x => x.MpPaymentId).IsUnique().HasFilter("\"MpPaymentId\" IS NOT NULL");
    }

    private static void ConfigurarNotificacao(EntityTypeBuilder<Notificacao> n)
    {
        n.ToTable("NotificacoesOutbox");
        n.HasKey(x => x.Id);
        n.Property(x => x.Id).ValueGeneratedNever();
        n.Property(x => x.Destinatario).HasMaxLength(200);
        n.Property(x => x.Assunto).HasMaxLength(300);
        n.HasOne<Orcamento>().WithMany().HasForeignKey(x => x.OrcamentoId).OnDelete(DeleteBehavior.Cascade);
        n.HasIndex(x => x.CriadaEm).HasFilter("\"EnviadaEm\" IS NULL");
    }
}
