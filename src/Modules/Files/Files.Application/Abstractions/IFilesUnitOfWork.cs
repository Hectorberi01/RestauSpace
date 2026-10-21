namespace Files.Application.Abstractions;

/// <summary>
/// Unit of work du module Files : une transaction sur le schéma propre au module, domain events écrits dans son
/// outbox. Jamais partagé avec un autre module.
/// </summary>
public interface IFilesUnitOfWork : IUnitOfWork;
