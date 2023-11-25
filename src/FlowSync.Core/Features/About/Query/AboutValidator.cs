﻿using FluentValidation;

namespace FlowSync.Core.Features.About.Query;

public class AboutValidator : AbstractValidator<AboutRequest>
{
    public AboutValidator()
    {
        RuleFor(request => request.Path)
            .NotNull()
            .NotEmpty()
            .WithMessage(FlowSyncCoreResource.AboutValidatorPathValueMustNotNullOrEmptyMessage);
    }
}