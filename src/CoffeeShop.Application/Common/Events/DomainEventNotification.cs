using CoffeeShop.Domain.Common;
using MediatR;

namespace CoffeeShop.Application.Common.Events;

public sealed record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent)
    : INotification
    where TDomainEvent : IDomainEvent;
