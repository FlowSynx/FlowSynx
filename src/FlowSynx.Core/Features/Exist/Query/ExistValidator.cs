﻿using FluentValidation;

namespace FlowSynx.Core.Features.Exist.Query;

public class ExistValidator : AbstractValidator<ExistRequest>
{
    public ExistValidator()
    {
        RuleFor(request => request.Path)
            .NotNull()
            .NotEmpty()
            .WithMessage(Resources.ListValidatorPathValueMustNotNullOrEmptyMessage);
    }
}