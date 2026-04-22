namespace NativeLambdaRouter.Tests;

public class ExceptionsTests
{
    [Fact]
    public void ValidationException_ShouldStoreMessage()
    {
        // Act
        var exception = new ValidationException("Field is required");

        // Assert
        exception.Message.ShouldBe("Field is required");
    }

    [Fact]
    public void ValidationException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ValidationException("Validation failed", innerException);

        // Assert
        exception.InnerException.ShouldBe(innerException);
    }

    [Fact]
    public void NotFoundException_ShouldStoreMessage()
    {
        // Act
        var exception = new NotFoundException("Item not found");

        // Assert
        exception.Message.ShouldBe("Item not found");
    }

    [Fact]
    public void NotFoundException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new NotFoundException("Not found", innerException);

        // Assert
        exception.InnerException.ShouldBe(innerException);
    }

    [Fact]
    public void UnauthorizedException_ShouldStoreMessage()
    {
        // Act
        var exception = new UnauthorizedException("Invalid token");

        // Assert
        exception.Message.ShouldBe("Invalid token");
    }

    [Fact]
    public void UnauthorizedException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new UnauthorizedException("Unauthorized", innerException);

        // Assert
        exception.InnerException.ShouldBe(innerException);
    }

    [Fact]
    public void ForbiddenException_ShouldStoreMessage()
    {
        // Act
        var exception = new ForbiddenException("Access denied");

        // Assert
        exception.Message.ShouldBe("Access denied");
    }

    [Fact]
    public void ForbiddenException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ForbiddenException("Forbidden", innerException);

        // Assert
        exception.InnerException.ShouldBe(innerException);
    }

    [Fact]
    public void ConflictException_ShouldStoreMessage()
    {
        // Act
        var exception = new ConflictException("Resource already exists");

        // Assert
        exception.Message.ShouldBe("Resource already exists");
    }

    [Fact]
    public void ConflictException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ConflictException("Conflict", innerException);

        // Assert
        exception.InnerException.ShouldBe(innerException);
    }

    [Fact]
    public void TooManyRequestsException_ShouldStoreMessage()
    {
        // Act
        var exception = new TooManyRequestsException("Rate limit exceeded");

        // Assert
        exception.Message.ShouldBe("Rate limit exceeded");
        exception.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public void TooManyRequestsException_ShouldStoreRetryAfter()
    {
        // Act
        var exception = new TooManyRequestsException("Rate limit exceeded", TimeSpan.FromSeconds(30));

        // Assert
        exception.Message.ShouldBe("Rate limit exceeded");
        exception.RetryAfter.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TooManyRequestsException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new TooManyRequestsException("Rate limit exceeded", innerException);

        // Assert
        exception.InnerException.ShouldBe(innerException);
        exception.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public void TooManyRequestsException_ShouldStoreRetryAfterAndInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new TooManyRequestsException("Rate limit exceeded", TimeSpan.FromSeconds(42), innerException);

        // Assert
        exception.InnerException.ShouldBe(innerException);
        exception.RetryAfter.ShouldBe(TimeSpan.FromSeconds(42));
    }
}
