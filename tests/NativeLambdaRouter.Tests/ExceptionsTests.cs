namespace NativeLambdaRouter.Tests;

public class ExceptionsTests
{
    [Fact]
    public void ValidationException_ShouldStoreMessage()
    {
        // Act
        var exception = new ValidationException("Field is required");

        // Assert
        exception.Message.Should().Be("Field is required");
    }

    [Fact]
    public void ValidationException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ValidationException("Validation failed", innerException);

        // Assert
        exception.InnerException.Should().Be(innerException);
    }

    [Fact]
    public void NotFoundException_ShouldStoreMessage()
    {
        // Act
        var exception = new NotFoundException("Item not found");

        // Assert
        exception.Message.Should().Be("Item not found");
    }

    [Fact]
    public void NotFoundException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new NotFoundException("Not found", innerException);

        // Assert
        exception.InnerException.Should().Be(innerException);
    }

    [Fact]
    public void UnauthorizedException_ShouldStoreMessage()
    {
        // Act
        var exception = new UnauthorizedException("Invalid token");

        // Assert
        exception.Message.Should().Be("Invalid token");
    }

    [Fact]
    public void UnauthorizedException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new UnauthorizedException("Unauthorized", innerException);

        // Assert
        exception.InnerException.Should().Be(innerException);
    }

    [Fact]
    public void ForbiddenException_ShouldStoreMessage()
    {
        // Act
        var exception = new ForbiddenException("Access denied");

        // Assert
        exception.Message.Should().Be("Access denied");
    }

    [Fact]
    public void ForbiddenException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ForbiddenException("Forbidden", innerException);

        // Assert
        exception.InnerException.Should().Be(innerException);
    }

    [Fact]
    public void ConflictException_ShouldStoreMessage()
    {
        // Act
        var exception = new ConflictException("Resource already exists");

        // Assert
        exception.Message.Should().Be("Resource already exists");
    }

    [Fact]
    public void ConflictException_ShouldStoreInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ConflictException("Conflict", innerException);

        // Assert
        exception.InnerException.Should().Be(innerException);
    }
}
